# Módulos

Cada módulo es una carpeta con sus slices verticales. Estructura:

```
Modules/<Nombre>/
├── Contracts/        # ÚNICA superficie pública: interfaces y DTOs que otros módulos pueden usar
├── Domain/           # entidades (implementan ITenantEntity, ITenantRoot o IGlobalEntity)
├── Persistence/      # IEntityTypeConfiguration<T> (tablas en snake_case)
├── Features/         # un archivo por caso de uso: endpoint + request + validator + handler
└── <Nombre>Module.cs # Add<Nombre>Module (DI) y Map<Nombre>Endpoints (rutas)
```

Registrar el módulo en [ModuleRegistry.cs](ModuleRegistry.cs). Las reglas las controlan los tests en
`tests/Api.Tests/Architecture`.

| Módulo         | Estado      | Responsabilidad |
|----------------|-------------|-----------------|
| `Businesses`   | ✅ creado    | Negocio (tenant): nombre, slug, zona horaria, moneda |
| `Users`        | ✅ creado    | Usuarios, membresías y roles por negocio, autenticación |
| `Services`     | ✅ creado    | Catálogo de servicios: duración, precio (`Money`) |
| `Staff`        | pendiente   | Profesionales y qué servicios realizan |
| `Schedules`    | pendiente   | Horarios de atención y de cada profesional, excepciones/feriados |
| `Appointments` | pendiente   | Turnos (exclusion constraint con `btree_gist` contra superposición) |
| `Booking`      | pendiente   | Web pública de reservas: disponibilidad, reserva, seña |
| `Payments`     | pendiente   | Cobros y señas (Mercado Pago). Inmutables |
| `CashRegister` | pendiente   | Caja: aperturas, cierres, movimientos. Inmutables |
| `Customers`    | pendiente   | Clientes del negocio |
