# FIXY - Clean Architecture Base Project

Cấu trúc dự án theo Clean Architecture pattern cho .NET 8 với Entity Framework Core.

## 📁 Cấu Trúc Thư Mục

```
FIXY/
│
├── 📦 Domain/                    # Core Layer - Không phụ thuộc gì
│   ├── Common/                  # Base classes, interfaces chung
│   │   ├── BaseEntity.cs
│   │   ├── BaseAuditableEntity.cs
│   │   ├── IAuditableEntity.cs
│   │   └── ISoftDelete.cs
│   ├── Entity/                  # Domain entities (business objects)
│   ├── Enum/                    # Domain enums
│   ├── Events/                  # Domain events
│   │   └── IDomainEvent.cs
│   └── Exceptions/              # Domain exceptions
│       ├── BusinessException.cs
│       └── NotFoundException.cs
│
├── 📦 Application/              # Business Logic Layer
│   ├── Common/                  # Shared utilities
│   │   ├── Behaviors/           # MediatR pipeline behaviors (nếu cần)
│   │   ├── Interfaces/          # Application interfaces
│   │   │   ├── ICurrentUserService.cs
│   │   │   └── IDateTimeProvider.cs
│   │   ├── OperationResult.cs
│   │   ├── PagedQuery.cs
│   │   └── PagedResponse.cs
│   ├── DTOs/                    # Data Transfer Objects
│   ├── Interfaces/              # Application contracts
│   │   ├── IUnitOfWork.cs
│   │   ├── Repositories/        # Repository interfaces
│   │   │   └── IRepository.cs (Generic)
│   │   └── Services/            # Service interfaces
│   ├── Settings/                # Configuration settings
│   ├── DependencyInjection.cs   # DI registration
│   └── Service/                 # Service implementations (nếu không dùng MediatR)
│
├── 📦 Infrastructure/            # Implementation Layer
│   ├── Persistence/             # Database layer
│   │   ├── AppDbContext.cs      # EF Core DbContext
│   │   ├── AppDbContextFactory.cs
│   │   ├── UnitOfWork.cs        # Unit of Work pattern
│   │   ├── Configurations/      # EF Core configurations
│   │   ├── Migrations/          # EF Core migrations
│   │   └── Interceptors/        # EF Core interceptors
│   ├── Repositories/            # Repository implementations
│   │   └── Repository.cs        # Generic repository
│   ├── Services/                # Service implementations
│   ├── Common/                  # Infrastructure common
│   │   └── SystemDateTimeProvider.cs
│   └── DependencyInjection.cs   # DI registration
│
├── 📦 API/                      # Presentation Layer
│   ├── Controllers/              # API Controllers
│   │   └── ApiController.cs    # Base controller
│   ├── Contracts/               # API request/response models
│   ├── Middlewares/             # Custom middlewares
│   │   └── ExceptionMiddleware.cs
│   ├── Authorization/           # Authorization handlers
│   ├── Attributes/              # Custom attributes
│   ├── Responses/               # API response models
│   ├── Program.cs               # Application entry point
│   └── appsettings.json        # Configuration
│
├── 📁 docs/                     # Documentation
│   ├── CODING_GUIDE.md         # Chi tiết hướng dẫn code, naming, file structure
│   └── ...
│
├── 📁 scripts/                  # Utility scripts
│   └── SeedData.sql
│
└── FIXY.sln     # Solution file
```

## 🏗️ Kiến Trúc Layers

### 1. **Domain Layer** (Core - Không phụ thuộc)
- **Mục đích**: Chứa business entities và domain logic thuần túy
- **Không phụ thuộc**: Không reference đến layer nào khác
- **Chứa**:
  - Entities (Domain models)
  - Enums
  - Domain Events
  - Domain Exceptions
  - Base classes/interfaces (BaseEntity, IAuditableEntity, ISoftDelete)

### 2. **Application Layer** (Business Logic)
- **Mục đích**: Chứa business logic và use cases
- **Phụ thuộc**: Chỉ phụ thuộc Domain
- **Chứa**:
  - DTOs (Request/Response models)
  - Service Interfaces (contracts)
  - Repository Interfaces (contracts)
  - Common utilities (OperationResult, PagedQuery, PagedResponse)
  - Settings/Configuration classes

### 3. **Infrastructure Layer** (Implementation)
- **Mục đích**: Implement các interfaces từ Application
- **Phụ thuộc**: Application và Domain
- **Chứa**:
  - Repository Implementations (Generic Repository)
  - Service Implementations
  - Database (EF Core, DbContext với audit và soft delete)
  - External services
  - Migrations

### 4. **API Layer** (Presentation)
- **Mục đích**: Entry point, HTTP endpoints
- **Phụ thuộc**: Application và Infrastructure
- **Chứa**:
  - Controllers (Base ApiController)
  - Middlewares (Exception handling)
  - Authorization handlers
  - API Contracts
  - Program.cs (Startup)

## 📋 Quy Tắc Dependency

```
API → Infrastructure → Application → Domain
  ↓         ↓              ↓
  └─────────┴──────────────┘
     (chỉ phụ thuộc Domain)
```

- **Domain**: Không phụ thuộc gì
- **Application**: Chỉ phụ thuộc Domain
- **Infrastructure**: Phụ thuộc Application và Domain
- **API**: Phụ thuộc tất cả layers

## 📝 File Base Đã Có Sẵn

### Domain Layer
- ✅ `BaseEntity.cs` - Base class với Id, CreatedDate, UpdatedDate
- ✅ `BaseAuditableEntity.cs` - Base class với audit fields (CreatedBy, UpdatedBy)
- ✅ `IAuditableEntity.cs` - Interface cho audit
- ✅ `ISoftDelete.cs` - Interface cho soft delete
- ✅ `IDomainEvent.cs` - Interface cho domain events
- ✅ `BusinessException.cs` - Domain exception
- ✅ `NotFoundException.cs` - Not found exception

### Application Layer
- ✅ `OperationResult<T>.cs` - Generic result wrapper
- ✅ `PagedQuery.cs` - Base class cho paged queries
- ✅ `PagedResponse<T>.cs` - Paged response model
- ✅ `ICurrentUserService.cs` - Interface để lấy current user
- ✅ `IDateTimeProvider.cs` - Interface để lấy datetime (dễ test)
- ✅ `IUnitOfWork.cs` - Unit of Work interface
- ✅ `IRepository<T>.cs` - Generic repository interface
- ✅ `DependencyInjection.cs` - DI registration

### Infrastructure Layer
- ✅ `AppDbContext.cs` - EF Core DbContext với:
  - Global query filter cho soft delete
  - Auto-set audit fields (CreatedDate, UpdatedDate)
  - Auto-handle soft delete (set IsDeleted thay vì delete)
- ✅ `AppDbContextFactory.cs` - Factory cho EF Core migrations
- ✅ `UnitOfWork.cs` - Unit of Work implementation
- ✅ `Repository<T>.cs` - Generic repository implementation
- ✅ `SystemDateTimeProvider.cs` - DateTime provider implementation
- ✅ `DependencyInjection.cs` - DI registration

### API Layer
- ✅ `ApiController.cs` - Base controller với helper methods
- ✅ `ExceptionMiddleware.cs` - Global exception handling
- ✅ `Program.cs` - Application startup với DI setup

## 🚀 Cách Sử Dụng

### 1. Tạo Entity mới

```csharp
// Domain/Entity/Employee.cs
using Domain.Common;

namespace Domain.Entity
{
    public class Employee : BaseAuditableEntity, ISoftDelete
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
        public DateTime? DeletedDate { get; set; }
        public string? DeletedBy { get; set; }
    }
}
```

### 2. Tạo EF Core Configuration

```csharp
// Infrastructure/Persistence/Configurations/EmployeeConfiguration.cs
using Domain.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations
{
    public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
    {
        public void Configure(EntityTypeBuilder<Employee> builder)
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
            builder.Property(e => e.Email).IsRequired().HasMaxLength(200);
            builder.HasIndex(e => e.Email).IsUnique();
        }
    }
}
```

### 3. Thêm DbSet vào AppDbContext

```csharp
public DbSet<Employee> Employees { get; set; }
```

### 4. Tạo Repository Interface (nếu cần custom)

```csharp
// Application/Interfaces/Repositories/IEmployeeRepository.cs
using Application.Interfaces.Repositories;
using Domain.Entity;

namespace Application.Interfaces.Repositories
{
    public interface IEmployeeRepository : IRepository<Employee>
    {
        Task<Employee?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    }
}
```

### 5. Implement Repository

```csharp
// Infrastructure/Repositories/EmployeeRepository.cs
using Application.Interfaces.Repositories;
using Domain.Entity;
using Infrastructure.Persistence;

namespace Infrastructure.Repositories
{
    public class EmployeeRepository : Repository<Employee>, IEmployeeRepository
    {
        public EmployeeRepository(AppDbContext context) : base(context)
        {
        }

        public async Task<Employee?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            return await _dbSet.FirstOrDefaultAsync(e => e.Email == email, cancellationToken);
        }
    }
}
```

### 6. Tạo Service Interface

```csharp
// Application/Interfaces/Services/IEmployeeService.cs
using Application.Common;
using Domain.Entity;

namespace Application.Interfaces.Services
{
    public interface IEmployeeService
    {
        Task<OperationResult<Employee>> GetByIdAsync(int id);
        Task<OperationResult<List<Employee>>> GetAllAsync();
        Task<OperationResult<Employee>> CreateAsync(Employee employee);
    }
}
```

### 7. Implement Service

```csharp
// Infrastructure/Services/EmployeeService.cs
using Application.Common;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entity;
using Domain.Exceptions;

namespace Infrastructure.Services
{
    public class EmployeeService : IEmployeeService
    {
        private readonly IEmployeeRepository _repository;
        private readonly IUnitOfWork _unitOfWork;

        public EmployeeService(IEmployeeRepository repository, IUnitOfWork unitOfWork)
        {
            _repository = repository;
            _unitOfWork = unitOfWork;
        }

        public async Task<OperationResult<Employee>> GetByIdAsync(int id)
        {
            var employee = await _repository.GetByIdAsync(id);
            if (employee == null)
            {
                return OperationResult<Employee>.Failure("Employee not found");
            }
            return OperationResult<Employee>.Success(employee);
        }

        public async Task<OperationResult<List<Employee>>> GetAllAsync()
        {
            var employees = await _repository.GetAllAsync();
            return OperationResult<List<Employee>>.Success(employees);
        }

        public async Task<OperationResult<Employee>> CreateAsync(Employee employee)
        {
            await _repository.AddAsync(employee);
            await _unitOfWork.SaveChangesAsync();
            return OperationResult<Employee>.Success(employee, "Employee created successfully");
        }
    }
}
```

### 8. Đăng ký DI

```csharp
// Infrastructure/DependencyInjection.cs - thêm vào
services.AddScoped<IEmployeeRepository, EmployeeRepository>();
services.AddScoped<IEmployeeService, EmployeeService>();
```

### 9. Tạo Controller

```csharp
// API/Controllers/EmployeesController.cs
using Application.Common;
using Application.Interfaces.Services;
using Domain.Entity;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    public class EmployeesController : ApiController
    {
        private readonly IEmployeeService _service;

        public EmployeesController(IEmployeeService service)
        {
            _service = service;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _service.GetByIdAsync(id);
            return HandleResult(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _service.GetAllAsync();
            return HandleResult(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Employee employee)
        {
            var result = await _service.CreateAsync(employee);
            return HandleResult(result);
        }
    }
}
```

## 🔄 Flow Xử Lý Request

```
Client Request
    ↓
API/Controllers (API Layer)
    ↓
Application/Interfaces/Services (Application Layer - Interface)
    ↓
Infrastructure/Services (Infrastructure Layer - Implementation)
    ↓
Infrastructure/Repositories (Infrastructure Layer)
    ↓
Infrastructure/Persistence/AppDbContext (Database)
    ↓
Domain/Entity (Domain Layer)
```

## 📦 Database Configuration

Có thể sử dụng SQL Server hoặc InMemory:
- Nếu có `ConnectionStrings:DefaultConnection` trong appsettings.json → dùng SQL Server
- Nếu không có → tự động dùng InMemory database

## 🎯 Tính Năng Sẵn Có

### Audit Fields
- Tự động set `CreatedDate`, `UpdatedDate` khi save changes
- Hỗ trợ `CreatedBy`, `UpdatedBy` qua `IAuditableEntity`

### Soft Delete
- Tự động filter entities đã bị xóa trong queries
- Khi delete, sẽ set `IsDeleted = true` thay vì xóa thật

### Exception Handling
- Global exception middleware
- `BusinessException` → BadRequest (400)
- `NotFoundException` → NotFound (404)
- Other exceptions → InternalServerError (500)

### Generic Repository
- CRUD operations cơ bản
- Query methods (Find, FirstOrDefault, Exists, Count)
- Hỗ trợ pagination và filtering

## 🚀 Cách Chạy

```bash
dotnet restore
dotnet build
dotnet run --project API
```

Truy cập Swagger: `https://localhost:7059/swagger`

## 🐳 Triển Khai Tự Host (Docker)

Ngoài Azure App Service (workflow `.github/workflows/deploy.yml`, nhánh `master`), API có thể tự host trên một VPS Ubuntu bằng Docker:

- `Dockerfile`: build nhiều tầng, chạy bằng user không phải root ở cổng 8080, kiểm tra sức khoẻ tại `GET /health`.
- `docker-compose.yml`: gồm `api`, SQL Server 2022 Express, Redis, Cloudflare Tunnel, cùng service `migrate` để chạy migration.
- `.env.example`: danh sách đầy đủ các biến cấu hình.
- `deploy/`: hướng dẫn từng bước ([deploy/README.md](deploy/README.md)) và các script migrate, đặt mật khẩu admin, sao lưu.

Lưu ảnh: đặt `Storage__Provider=Local` để lưu ảnh trên VPS (ảnh CCCD và ảnh khuôn mặt chỉ trả cho chính chủ hoặc ADMIN), hoặc `AzureBlob` để giữ cách cũ.

## 📦 Packages Đã Cài Đặt

- **Domain**: Không có dependencies
- **Application**: AutoMapper.Extensions.Microsoft.DependencyInjection
- **Infrastructure**: 
  - EntityFrameworkCore.SqlServer
  - EntityFrameworkCore.InMemory
  - EntityFrameworkCore.Design
  - Microsoft.Extensions.Configuration
- **API**: Swashbuckle.AspNetCore (Swagger)

## 🎯 Nguyên Tắc

1. **Dependency Rule**: Inner layers không biết outer layers
2. **Interface Segregation**: Application định nghĩa interfaces, Infrastructure implement
3. **Single Responsibility**: Mỗi class/service có một trách nhiệm
4. **Separation of Concerns**: Tách biệt rõ ràng giữa các layers
