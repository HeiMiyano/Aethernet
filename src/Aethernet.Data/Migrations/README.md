# Migrations

EF Core migrations land here. To create the first one:

```bash
cd src
dotnet tool restore   # if dotnet-ef isn't already installed globally
dotnet ef migrations add Initial -p Aethernet.Data -s Aethernet.Server
dotnet ef database update -p Aethernet.Data -s Aethernet.Server
```

The `Aethernet.Data` project hosts the migrations assembly. `Aethernet.Server` is used as the
startup project so the connection string from `appsettings.Development.json` is picked up. You can
also override the connection at design time with the `AETHERNET_DB` env var (see
`AethernetDbContextFactory`).

> Generated migration `.cs` files are not committed in this scaffold — run the command above on
> first checkout to materialize them.
