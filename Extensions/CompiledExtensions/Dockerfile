# Use the official Postgres 15 image as the base
FROM postgres:15

# Copy your extension files into the correct Postgres directories

# Cart Extension
COPY cart_ext.so /usr/lib/postgresql/15/lib/
COPY cart_ext.control /usr/share/postgresql/15/extension/
COPY cart_ext--0.0.0.sql /usr/share/postgresql/15/extension/

# Customer Extension
COPY customer_ext.so /usr/lib/postgresql/15/lib/
COPY customer_ext.control /usr/share/postgresql/15/extension/
COPY customer_ext--0.0.0.sql /usr/share/postgresql/15/extension/

# Order Extension
COPY order_ext.so /usr/lib/postgresql/15/lib/
COPY order_ext.control /usr/share/postgresql/15/extension/
COPY order_ext--0.0.0.sql /usr/share/postgresql/15/extension/

# Payment Extension
COPY payment_ext.so /usr/lib/postgresql/15/lib/
COPY payment_ext.control /usr/share/postgresql/15/extension/
COPY payment_ext--0.0.0.sql /usr/share/postgresql/15/extension/

# Product Extension
COPY product_ext.so /usr/lib/postgresql/15/lib/
COPY product_ext.control /usr/share/postgresql/15/extension/
COPY product_ext--0.0.0.sql /usr/share/postgresql/15/extension/

# Seller Extension
COPY seller_ext.so /usr/lib/postgresql/15/lib/
COPY seller_ext.control /usr/share/postgresql/15/extension/
COPY seller_ext--0.0.0.sql /usr/share/postgresql/15/extension/

# Shipment Extension
COPY shipment_ext.so /usr/lib/postgresql/15/lib/
COPY shipment_ext.control /usr/share/postgresql/15/extension/
COPY shipment_ext--0.0.0.sql /usr/share/postgresql/15/extension/

# Stock Extension
COPY stock_ext.so /usr/lib/postgresql/15/lib/
COPY stock_ext.control /usr/share/postgresql/15/extension/
COPY stock_ext--0.0.0.sql /usr/share/postgresql/15/extension/

COPY test_ext.so /usr/lib/postgresql/15/lib/
COPY test_ext.control /usr/share/postgresql/15/extension/
COPY test_ext--0.0.0.sql /usr/share/postgresql/15/extension/

# Set the default Postgres password (for user 'postgres')
ENV POSTGRES_PASSWORD=password

# Expose PostgreSQL's default port
EXPOSE 5432