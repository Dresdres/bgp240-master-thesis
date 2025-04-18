/* <begin connected objects> */
/*
This file is auto generated by pgrx.

The ordering of items is not stable, it is driven by a dependency graph.
*/
/* </end connected objects> */

/* <begin connected objects> */
-- src/lib.rs:17
-- stock_ext::setup_stock
CREATE  FUNCTION "setup_stock"() RETURNS VOID /* core::result::Result<(), pgrx::spi::SpiError> */
STRICT
LANGUAGE c /* Rust */
AS 'MODULE_PATHNAME', 'setup_stock_wrapper';
/* </end connected objects> */

/* <begin connected objects> */
-- src/lib.rs:44
-- stock_ext::stock_add_checkout_transaction_mark
CREATE  FUNCTION "stock_add_checkout_transaction_mark"(
	"stream_id" TEXT, /* &str */
	"instance_id" TEXT, /* &str */
	"transaction_type" TEXT, /* &str */
	"customer_id" TEXT, /* &str */
	"mark_status" TEXT, /* &str */
	"db" TEXT /* &str */
) RETURNS VOID /* core::result::Result<(), pgrx::spi::SpiError> */
STRICT
LANGUAGE c /* Rust */
AS 'MODULE_PATHNAME', 'stock_add_checkout_transaction_mark_wrapper';
/* </end connected objects> */

/* <begin connected objects> */
-- src/lib.rs:73
-- stock_ext::stock_add_product_transaction_mark
CREATE  FUNCTION "stock_add_product_transaction_mark"(
	"stream_id" TEXT, /* &str */
	"instance_id" TEXT, /* &str */
	"transaction_type" TEXT, /* &str */
	"seller_id" TEXT, /* &str */
	"mark_status" TEXT, /* &str */
	"db" TEXT /* &str */
) RETURNS VOID /* core::result::Result<(), pgrx::spi::SpiError> */
STRICT
LANGUAGE c /* Rust */
AS 'MODULE_PATHNAME', 'stock_add_product_transaction_mark_wrapper';
/* </end connected objects> */

/* <begin connected objects> */
-- src/lib.rs:117
-- stock_ext::stock_listen_to_changes
CREATE  FUNCTION "stock_listen_to_changes"() RETURNS VOID /* core::result::Result<(), alloc::string::String> */
STRICT
LANGUAGE c /* Rust */
AS 'MODULE_PATHNAME', 'stock_listen_to_changes_wrapper';
/* </end connected objects> */

