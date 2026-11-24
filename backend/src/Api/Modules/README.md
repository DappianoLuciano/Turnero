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
| `Staff`        | ✅ creado    | Profesionales y qué servicios realizan |
| `Schedules`    | ✅ creado    | Horarios semanales por profesional (tramos), días cerrados y disponibilidad |
| `Appointments` | ✅ creado    | Turnos (exclusion constraint con `btree_gist` contra superposición) |
| `Booking`      | ✅ creado    | Web pública de reservas sin cuenta: catálogo, horarios libres y reserva (seña: pendiente); cambio y cancelación por enlace (ADR 0015) |
| `Payments`     | ✅ creado    | Cobros de turnos, append-only con anulaciones (Mercado Pago y señas: pendiente) |
| `CashRegister` | ✅ creado    | Caja: efectivo esperado del día (cobros en efectivo + movimientos manuales), cierre diario. Append-only |
| `Customers`    | ✅ creado    | Clientes del negocio |
| `Subscriptions`| ✅ creado    | Prueba gratis (2 días) y períodos pagos; bloquea el negocio al vencer (ADR 0014) |
| `Signup`       | ✅ creado    | Alta de negocios y dueños sin intervención (rate limit por IP) |
