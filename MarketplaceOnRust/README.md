# MarketplaceDapr

## How to setup the environment


## How to run

## How to trigger transactions


## Useful links

### Killing Postgres instances
https://github.com/PostgresApp/PostgresApp/issues/96#issuecomment-24462678

### Killing Dapr process
https://stackoverflow.com/questions/11583562/how-to-kill-a-process-running-on-particular-port-in-linux

### Health checks
could enable dapr health check instead of aspnet, but since it is preview feature, better keep like this
https://docs.dapr.io/developing-applications/building-blocks/observability/app-health/
https://www.ibm.com/garage/method/practices/manage/health-check-apis/
https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments?view=aspnetcore-7.0#set-environment-on-the-command-line
https://laurentkempe.com/2023/02/27/debugging-dapr-applications-with-rider-or-visual-studio-a-better-way/

### Retry policies
https://docs.dapr.io/operations/resiliency/policies

#### Dead letter queue
https://docs.dapr.io/developing-applications/building-blocks/pubsub/pubsub-deadletter/

### Metrics
https://docs.dapr.io/operations/monitoring/metrics/metrics-overview/
https://docs.dapr.io/operations/monitoring/metrics/prometheus/
https://github.com/RicardoNiepel/dapr-docs/blob/master/howto/setup-monitoring-tools/observe-metrics-with-prometheus-locally.md
https://prometheus.io/docs/prometheus/latest/getting_started/

### Docker
https://stackoverflow.com/questions/40513545/how-to-prevent-docker-from-starting-a-container-automatically-on-system-startup

## Virtualized deployments
https://mikehadlow.com/posts/2022-06-24-writing-dotnet-services-for-kubernetes/

### PostgreSQL
https://dba.stackexchange.com/questions/274788/postgres-show-max-connections-output-a-different-value-from-postgresql-conf
https://stackoverflow.com/questions/8288823/query-a-parameter-postgresql-conf-setting-like-max-connections

### Redis
https://stackoverflow.com/questions/28785383/how-to-disable-persistence-with-redis

### Running bash script
https://unix.stackexchange.com/questions/27054/bin-bash-no-such-file-or-directory