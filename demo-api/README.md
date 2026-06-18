# demo-api

Real-world .NET 10 minimal Web API with actual Entity Framework Core CRUD operations. Demonstrates production error patterns by performing authentic database operations against MSSQL or SQLite. Created in Phase 8 of [plan.md](../plan.md); refactored in Phase 10 for realistic CRUD workflows.

## Database Setup

- **Default Provider**: SQL Server (`HFA_Database__Provider=SqlServer`)
- **Production Setup**: Dockerized MSSQL at `localhost,14333` (see [infra/mssql/README.md](../infra/mssql/README.md))
- **Fallback**: SQLite in-memory by setting `HFA_Database__Provider=Sqlite`

## Real CRUD Endpoints (Phase 10 Refactor)

All endpoints now perform **real Entity Framework Core operations** without mocking:

### 1. **POST /orders** 
- **Operation**: Real customer creation and order insert
- **Models**: `CustomerRecord`, `OrderRecord`
- **CRUD**: INSERT customer (if new), INSERT order
- **Database**: Real EF SaveChangesAsync

### 2. **GET /payments/{id}** 
- **Operation**: Real payment lookup by ID
- **Models**: `PaymentRecord`
- **CRUD**: SELECT payment record
- **Failure**: Payment not found (InvalidOperationException)

### 3. **GET /payments/{id}/settlement** 
- **Operation**: Real settlement status query
- **CRUD**: SELECT payment settlement status
- **Failure**: Settlement record not found

### 4. **GET /users/{id}** 
- **Operation**: Real user lookup by ID
- **Models**: `UserRecord`
- **CRUD**: SELECT user record
- **Failure**: ArgumentOutOfRangeException on negative ID

### 5. **POST /invoices/duplicate** 
- **Operation**: Real duplicate invoice insertion (triggers constraint)
- **Models**: `InvoiceRecord`
- **CRUD**: INSERT invoice x2 with same invoice number
- **Database Failure**: Real MSSQL unique constraint violation
- **Exception**: `DbUpdateException` from actual SQL Server

### 6. **GET /invoices/reprice** 
- **Operation**: Real invoice query and projection
- **Models**: `InvoiceRecord`
- **CRUD**: SELECT invoices
- **Potential Failure**: Query timeout on large datasets

### 7. **POST /transfers/on-hold** 
- **Operation**: Real transfer creation when SQL limit reached
- **Models**: `TransferRecord`
- **CRUD**: INSERT transfer record with on-hold status
- **Realistic Scenario**: From support email - transfers held due to database resource exhaustion
- **Message**: `"Error = Sql; Limit reached while applying payments to customer wallets"`

### 8. **GET /pricing/preview** 
- **Operation**: Real pricing calculation and storage
- **Models**: `PricingRecord`
- **CRUD**: CREATE pricing record with computed final amount
- **Business Logic**: Validates loyalty multiplier (0-1 range)
- **Failure**: InvalidOperationException on invalid multiplier

### 9. **GET /health** 
- Simple liveness probe

## Entity Models (Real Database Schema)

```csharp
public sealed class CustomerRecord { Id, Name, Email, CreatedAtUtc }
public sealed class OrderRecord { Id, OrderNumber, CustomerId, Amount, CreatedAtUtc }
public sealed class PaymentRecord { Id, PaymentId, CustomerId, Amount, Status, CreatedAtUtc }
public sealed class UserRecord { Id, Name, Email, CreatedAtUtc }
public sealed class InvoiceRecord { Id, InvoiceNumber, CustomerId, Amount, CreatedAtUtc }
public sealed class TransferRecord { Id, TransferId, CustomerId, Amount, Status, PaymentIds, Rails, CreatedAtUtc }
public sealed class PricingRecord { Id, Subtotal, LoyaltyMultiplier, FinalAmount, CreatedAtUtc }
```

## No Mocking - Real Operations

✅ **Real Entity Framework Core DbContext**  
✅ **Real Database Constraints** (unique indexes, foreign keys)  
✅ **Real SQL Server Errors** (MSB3026, duplicate key, etc.)  
✅ **Real Async/Await Patterns** (async SaveChangesAsync, ToListAsync)  
✅ **Real Connection Pooling** (retry logic, command timeout)  
✅ **Real Structured Logging** (Serilog with ECS format)  
❌ No mock repositories  
❌ No in-memory data stores  
❌ No simulated exceptions (genuine DB operations)

