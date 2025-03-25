use pgrx::prelude::*;

::pgrx::pg_module_magic!();


#[pg_extern]
fn setup_test() -> Result<(), spi::Error> {
//await this.daprClient.PublishEventAsync(PUBSUB_NAME, priceUpdateStreamId, new TransactionMark(priceUpdate.instanceId, TransactionType.PRICE_UPDATE, priceUpdate.seller_id, MarkStatus.SUCCESS, "cart"));
    let create_product_sql = r#"
        CREATE OR REPLACE FUNCTION notify_insert_productupdate()
        RETURNS TRIGGER AS
        $$
        DECLARE
        payload TEXT;
        BEGIN
        payload := json_build_object(
            'tid', NEW.instance_id,
            'type', NEW.transaction_type::TEXT,
            'actorId', NEW.seller_id::INT,
            'status', NEW.mark_status,
            'source', NEW.db
        )::text;

        EXECUTE format('NOTIFY productupdatemark, %L', payload);
        RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;

        CREATE TRIGGER productupdate_trigger
        AFTER INSERT ON PRODUCTUPDATE
        FOR EACH ROW
        EXECUTE FUNCTION notify_insert_productupdate();
    "#;

    Spi::run(create_product_sql)?;

    let create_price_sql = r#"
        CREATE OR REPLACE FUNCTION notify_insert_priceupdate()
        RETURNS TRIGGER AS
        $$
        DECLARE
        payload TEXT;
        BEGIN
        payload := json_build_object(
            'tid', NEW.instance_id,
            'type', NEW.transaction_type::TEXT,
            'actorId', NEW.seller_id::INT,
            'status', NEW.mark_status,
            'source', NEW.db
        )::text;

        EXECUTE format('NOTIFY priceupdatemark, %L', payload);
        RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;

        CREATE TRIGGER priceupdate_trigger
        AFTER INSERT ON PRICEUPDATE
        FOR EACH ROW
        EXECUTE FUNCTION notify_insert_priceupdate();
    "#;

    Spi::run(create_price_sql)?;

    let create_checkout_sql = r#"
        CREATE OR REPLACE FUNCTION notify_insert_checkout()
        RETURNS TRIGGER AS
        $$
        DECLARE
        payload TEXT;
        BEGIN
        payload := json_build_object(
            'tid', NEW.instance_id,
            'type', NEW.transaction_type::TEXT,
            'actorId', NEW.customer_id::INT,
            'status', NEW.mark_status,
            'source', NEW.db
        )::text;

        EXECUTE format('NOTIFY checkoutmark, %L', payload);
        RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;

        CREATE TRIGGER checkout_trigger
        AFTER INSERT ON CHECKOUT
        FOR EACH ROW
        EXECUTE FUNCTION notify_insert_checkout();
    "#;
    Spi::run(create_checkout_sql)?;

    Ok(())
}