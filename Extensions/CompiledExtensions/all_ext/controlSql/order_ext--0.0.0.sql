/* <begin connected objects> */
/*
This file is auto generated by pgrx.

The ordering of items is not stable, it is driven by a dependency graph.
*/
/* </end connected objects> */

/* <begin connected objects> */
-- src/lib.rs:37
-- order_ext::order_add_checkout_transaction_mark
CREATE  FUNCTION "order_add_checkout_transaction_mark"(
	"stream_id" TEXT, /* &str */
	"instance_id" TEXT, /* &str */
	"transaction_type" TEXT, /* &str */
	"customer_id" TEXT, /* &str */
	"mark_status" TEXT, /* &str */
	"db" TEXT /* &str */
) RETURNS VOID /* core::result::Result<(), pgrx::spi::SpiError> */
STRICT
LANGUAGE c /* Rust */
AS 'MODULE_PATHNAME', 'order_add_checkout_transaction_mark_wrapper';
/* </end connected objects> */

/* <begin connected objects> */
-- src/lib.rs:71
-- order_ext::order_listen_to_changes
CREATE  FUNCTION "order_listen_to_changes"() RETURNS VOID /* core::result::Result<(), alloc::string::String> */
STRICT
LANGUAGE c /* Rust */
AS 'MODULE_PATHNAME', 'order_listen_to_changes_wrapper';
/* </end connected objects> */

/* <begin connected objects> */
-- src/lib.rs:17
-- order_ext::setup_order
CREATE  FUNCTION "setup_order"() RETURNS VOID /* core::result::Result<(), pgrx::spi::SpiError> */
STRICT
LANGUAGE c /* Rust */
AS 'MODULE_PATHNAME', 'setup_order_wrapper';
/* </end connected objects> */

