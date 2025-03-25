use pgrx::prelude::*;
use pgrx::spi::{Spi, SpiError};

::pgrx::pg_module_magic!();

////////////////////////////////////////
// 1. Setup Tables
////////////////////////////////////////

#[pg_extern]
fn setup_product() -> Result<(), SpiError> {
    let create_price_sql = r#"
        CREATE TABLE IF NOT EXISTS PRICEUPDATE (
            stream_id TEXT NOT NULL,
            instance_id TEXT NOT NULL,
            transaction_type TEXT NOT NULL,
            seller_id TEXT NOT NULL,
            mark_status TEXT NOT NULL,
            db TEXT NOT NULL
        );
    "#;

    Spi::run(create_price_sql)?;
    Ok(())
}

////////////////////////////////////////
// 2. Transaction Mark Function
////////////////////////////////////////

#[pg_extern]
fn product_add_price_transaction_mark(
    stream_id: &str, instance_id: &str, transaction_type: &str,
    seller_id: &str, mark_status: &str, db: &str
) -> Result<(), SpiError> {
    let insert_sql = r#"
        INSERT INTO PRICEUPDATE (stream_id, instance_id, transaction_type, seller_id, mark_status, db)
        VALUES ($1, $2, $3, $4, $5, $6);
    "#;

    Spi::run_with_args(insert_sql, &[
        stream_id.into(), instance_id.into(), transaction_type.into(),
        seller_id.into(), mark_status.into(), db.into(),
    ])?;
    Ok(())
}
