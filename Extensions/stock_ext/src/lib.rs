use pgrx::{
    bgworkers::BackgroundWorkerBuilder,
    log, pg_extern, pg_guard,
    pg_sys::{self, panic::register_pg_guard_panic_hook},
    spi::{self, Spi, SpiError},
    FromDatum, IntoDatum,
};
use postgres::{fallible_iterator::FallibleIterator, Client, NoTls};
use std::time::Duration;

pgrx::pg_module_magic!();

////////////////////////////////////////
// 1. Setup and normal SQL-callable functions
////////////////////////////////////////

#[pg_extern]
fn setup_stock() -> Result<(), SpiError> {
    Spi::run(r#"
        CREATE TABLE IF NOT EXISTS CHECKOUT (
            stream_id TEXT NOT NULL,
            instance_id TEXT NOT NULL,
            transaction_type TEXT NOT NULL,
            mark_status TEXT NOT NULL,
            customer_id TEXT NOT NULL,
            db TEXT NOT NULL
        );
    "#)?;

    Spi::run(r#"
        CREATE TABLE IF NOT EXISTS PRODUCTUPDATE (
            stream_id TEXT NOT NULL,
            instance_id TEXT NOT NULL,
            transaction_type TEXT NOT NULL,
            seller_id TEXT NOT NULL,
            mark_status TEXT NOT NULL,
            db TEXT NOT NULL
        );
    "#)?;

    Ok(())
}

#[pg_extern]
fn stock_add_checkout_transaction_mark(
    stream_id: &str,
    instance_id: &str,
    transaction_type: &str,
    customer_id: &str,
    mark_status: &str,
    db: &str,
) -> Result<(), SpiError> {
    let insert_sql = r#"
        INSERT INTO CHECKOUT (stream_id, instance_id, transaction_type, customer_id, mark_status, db)
        VALUES ($1, $2, $3, $4, $5, $6);
    "#;

    Spi::run_with_args(
        insert_sql,
        &[
            stream_id.into(),
            instance_id.into(),
            transaction_type.into(),
            customer_id.into(),
            mark_status.into(),
            db.into(),
        ],
    )?;

    Ok(())
}

#[pg_extern]
fn stock_add_product_transaction_mark(
    stream_id: &str,
    instance_id: &str,
    transaction_type: &str,
    seller_id: &str,
    mark_status: &str,
    db: &str,
) -> Result<(), SpiError> {
    let insert_sql = r#"
        INSERT INTO PRODUCTUPDATE (stream_id, instance_id, transaction_type, seller_id, mark_status, db)
        VALUES ($1, $2, $3, $4, $5, $6);
    "#;

    Spi::run_with_args(
        insert_sql,
        &[
            stream_id.into(),
            instance_id.into(),
            transaction_type.into(),
            seller_id.into(),
            mark_status.into(),
            db.into(),
        ],
    )?;

    Ok(())
}

////////////////////////////////////////
// 2. Hardcode 5 channels + BGWs
////////////////////////////////////////

static CHANNELS: [(&str, &str); 5] = [
    ("price_changes", "stock_price_update_channel"),
    ("product_changes", "stock_product_update_channel"),
    ("checkout", "stock_checkout_update_channel"),
    ("payment_confirmed", "stock_payment_confirmed_channel"),
    ("payment_failed", "stock_payment_failed_channel"),
];

static CONN_INFO: &str =
    "host=localhost port=5432 dbname=postgres user=ucloud password=ucloud";

#[pg_extern]
fn stock_listen_to_changes() -> Result<(), String> {
    for (i, _) in CHANNELS.iter().enumerate() {
        spawn_listener(i as i32)?;
    }
    Ok(())
}

fn spawn_listener(id: i32) -> Result<(), String> {
    BackgroundWorkerBuilder::new("stock_listener")
        .set_library("stock_ext")
        .set_function("listen_bgworker")
        .enable_spi_access()
        .set_argument(id.into_datum())
        .load_dynamic();
    Ok(())
}

////////////////////////////////////////
// 3. The BGW main function, with extra logs
////////////////////////////////////////

#[pg_guard]
#[no_mangle]
pub extern "C" fn listen_bgworker(arg: pg_sys::Datum) {
    register_pg_guard_panic_hook();
    log!("BGW main: launched, got arg={arg:?}");

    unsafe {
        pg_sys::BackgroundWorkerInitializeConnection(
            "postgres\0".as_ptr() as *const i8,
            b"ucloud\0".as_ptr() as *const i8,  // username
            0,
        );
    }

    let id: Option<i32> = unsafe {
        i32::try_from_datum(arg, false, pg_sys::INT4OID)
            .expect("error converting BGW argument to i32")
    };

    match id {
        None => {
            log!("BGW main: invalid or null i32 argument => returning early");
            return;
        }
        Some(i) => {
            if i < 0 || (i as usize) >= CHANNELS.len() {
                log!("BGW main: invalid channel index {i} => returning early");
                return;
            }
            log!("BGW main: integer argument => {i}");
            run_bgworker(i);
        }
    }
}

fn run_bgworker(id: i32) {
    let (in_channel, out_channel) = CHANNELS[id as usize];
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



