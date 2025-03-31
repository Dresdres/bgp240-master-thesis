#!/usr/bin/env bash

sudo cp /work/big_data_systems_folder/andreas/Extensions/CompiledExtensions/all_ext/so/*.so /usr/lib/postgresql/15/lib/
sudo cp /work/big_data_systems_folder/andreas/Extensions/CompiledExtensions/all_ext/controlSql/*.sql /usr/share/postgresql/15/extension/
sudo cp /work/big_data_systems_folder/andreas/Extensions/CompiledExtensions/all_ext/controlSql/*.control /usr/share/postgresql/15/extension/

set -e

sudo chown postgres:postgres /usr/share/postgresql/15/extension/*.control
sudo chmod 644 /usr/share/postgresql/15/extension/*.control

sudo chown postgres:postgres /usr/share/postgresql/15/extension/*.sql
sudo chmod 644 /usr/share/postgresql/15/extension/*.sql

sudo chown postgres:postgres /usr/lib/postgresql/15/lib/*.so
sudo chmod 755 /usr/lib/postgresql/15/lib/*.so