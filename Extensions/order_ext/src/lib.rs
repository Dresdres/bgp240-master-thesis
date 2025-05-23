use pgrx::{
    bgworkers::BackgroundWorkerBuilder, log, pg_extern, pg_guard,
    pg_sys::{self, panic::register_pg_guard_panic_hook},
    spi::{self, Spi},
    FromDatum, IntoDatum,
};
use postgres::{fallible_iterator::FallibleIterator, Client, NoTls};
use std::time::Duration;

// Export PostgreSQL extension
pgrx::pg_module_magic!();

////////////////////////////////////////
// 1. Setup Tables
////////////////////////////////////////

#[pg_extern]
fn setup_order() -> Result<(), spi::Error> {
    let create_table_sql = r#"
        CREATE TABLE IF NOT EXISTS CHECKOUT (
            stream_id TEXT NOT NULL,
            instance_id TEXT NOT NULL,
            transaction_type TEXT NOT NULL,
            mark_status TEXT NOT NULL,
            customer_id TEXT NOT NULL,
            db TEXT NOT NULL
        );
    "#;
    Spi::run(create_table_sql)?;
    Ok(())
}

////////////////////////////////////////
// 2. Transaction Mark Functions
////////////////////////////////////////

#[pg_extern]
fn order_add_checkout_transaction_mark(
    stream_id: &str, instance_id: &str, transaction_type: &str,
    customer_id: &str, mark_status: &str, db: &str
) -> Result<(), spi::Error> {
    let insert_sql = r#"
        INSERT INTO CHECKOUT (stream_id, instance_id, transaction_type, customer_id, mark_status, db)
        VALUES ($1, $2, $3, $4, $5, $6);
    "#;
    Spi::run_with_args(insert_sql, &[
        stream_id.into(), instance_id.into(), transaction_type.into(),
        customer_id.into(), mark_status.into(), db.into(),
    ])?;
    Ok(())
}

////////////////////////////////////////
// 3. Background Workers (BGWs)
////////////////////////////////////////

static CHANNELS: [(&str, &str); 2] = [
    ("stock_confirmed", "order_stock_confirmed_channel"),
    ("shipment", "order_shipment_channel"),
];

static CONN_INFO: &str =
    "host=localhost port=5432 dbname=postgres user=ucloud password=ucloud";

const BGW_ID_OFFSET: i32 = 15;  // Ensure unique BGW IDs (15-16)

////////////////////////////////////////
// 4. Start BGWs
////////////////////////////////////////

#[pg_extern]
fn order_listen_to_changes() -> Result<(), String> {
    for (i, _) in CHANNELS.iter().enumerate() {
        spawn_listener(i as i32 + BGW_ID_OFFSET)?;
    }
    Ok(())
}

fn spawn_listener(id: i32) -> Result<(), String> {
    BackgroundWorkerBuilder::new("order_listener")
        .set_library("order_ext")
        .set_function("listen_bgworker")
        .enable_spi_access()
        .set_argument(id.into_datum())
        .load_dynamic();
    Ok(())
}

////////////////////////////////////////
// 5. BGW Entry Point
////////////////////////////////////////

#[pg_guard]
#[no_mangle]
pub extern "C" fn listen_bgworker(arg: pg_sys::Datum) {
    register_pg_guard_panic_hook();

    unsafe {
        pg_sys::BackgroundWorkerInitializeConnection(
            b"postgres\0".as_ptr() as *const i8,
            b"ucloud\0".as_ptr() as *const i8,
            0,
        );
    }

    let id: Option<i32> = unsafe { i32::try_from_datum(arg, false, pg_sys::INT4OID).ok().flatten() };
    match id {
        None => {
            log!("BGW main: invalid or null i32 argument => returning early");
            return;
        }
        Some(i) => {
            if i < BGW_ID_OFFSET || i >= BGW_ID_OFFSET + CHANNELS.len() as i32 {
                log!("BGW main: invalid channel index {i} => returning early");
                return;
            }
            run_bgworker(i);
        }
    }
}

////////////////////////////////////////
// 6. BGW Processing Loop
////////////////////////////////////////

fn run_bgworker(id: i32) {
    let (in_channel, out_channel) = CHANNELS[(id - BGW_ID_OFFSET) as usize];
    log!("BGW {id}: Starting, listening on `{in_channel}`, forwarding to `{out_channel}`");

    loop {
        pgrx::check_for_interrupts!();

        match Client::connect(CONN_INFO, NoTls) {
            Ok(mut client) => {
                let listen_stmt = format!("LISTEN {in_channel}");
                if let Err(e) = client.batch_execute(&listen_stmt) {
                    log!("BGW {id}: error LISTENing on `{in_channel}`: {e} -> sleep 5s");
                    std::thread::sleep(Duration::from_secs(5));
                    continue;
                }

                let mut notifications = client.notifications();
                loop {
                    pgrx::check_for_interrupts!();

                    match notifications.blocking_iter().next() {
                        Ok(Some(notification)) => {
                            let payload = notification.payload().to_string();

                            // Start a transaction
                            unsafe { pg_sys::StartTransactionCommand(); }
                            unsafe { pg_sys::PushActiveSnapshot(pg_sys::GetTransactionSnapshot()); }

                            // Notify via SPI
                            let notify_sql = format!("SELECT pg_notify('{out_channel}', $1)");
                            let spi_result = Spi::run_with_args(&notify_sql, &[payload.clone().into()]);

                            // Cleanup transaction
                            unsafe { pg_sys::PopActiveSnapshot(); }
                            unsafe { pg_sys::CommitTransactionCommand(); }

                            if let Err(e) = spi_result {
                                log!("BGW {id}: SPI error while NOTIFY: {e}");
                            }
                        }
                        Ok(None) => break,
                        Err(e) => {
                            log!("BGW {id}: error receiving from {in_channel}: {e} => break");
                            break;
                        }
                    }
                }
            }
            Err(e) => {
                log!("BGW {id}: cannot connect => {e} => sleep 5s");
                std::thread::sleep(Duration::from_secs(5));
                continue;
            }
        }
    }
}
