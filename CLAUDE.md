# CLAUDE.md — Backend (ASP.NET Core 8)

Instrucciones específicas para trabajar en la carpeta `backend/`.
Se combinan con las reglas del `CLAUDE.md` raíz.

---

## Stack

| Elemento       | Detalle                                      |
|----------------|----------------------------------------------|
| Framework      | ASP.NET Core 8                               |
| Lenguaje       | C# 12                                        |
| ORM            | Entity Framework Core (PostgreSQL)           |
| Auth           | JWT Bearer (access + refresh token)          |
| Validación     | FluentValidation (o DataAnnotations)         |
| Mensajería     | WhatsApp Business API (Meta)                 |
| DB             | PostgreSQL 16 (`Host=postgres` en Docker)    |

---

## Estructura esperada de capas

```
backend/
├── src/
│   ├── Domain/              ← Entidades, interfaces, value objects, enums
│   │                           SIN dependencias externas (ni EF, ni HTTP)
│   ├── Application/         ← Casos de uso, servicios de aplicación, DTOs
│   │                           Depende solo de Domain
│   ├── Infrastructure/      ← Repositorios EF, DbContext, servicios externos
│   │                           (WhatsApp, email, etc.)
│   └── API/                 ← Controllers, middlewares, configuración DI
│       (o WebAPI/)             Punto de entrada HTTP
├── tests/
├── Dockerfile
└── ...
```

---

## Comandos (ejecutar dentro del contenedor)

```bash
# Desde la raíz del mono-repo
docker compose exec backend bash

# Dentro del contenedor:
dotnet build
dotnet test
dotnet test --filter "Category=Unit"
dotnet format --verify-no-changes

# Migraciones EF Core
dotnet ef migrations add <NombreMigracion> \
  --project src/Infrastructure \
  --startup-project src/API

dotnet ef database update \
  --project src/Infrastructure \
  --startup-project src/API

dotnet ef migrations list \
  --project src/Infrastructure \
  --startup-project src/API
```

---

## Reglas específicas del backend

### Capas y dependencias
- `Domain` no importa EF Core, ni HttpClient, ni nada de infraestructura
- `Application` no conoce `DbContext` directamente — usa interfaces de repositorio
- `Infrastructure` implementa las interfaces definidas en `Domain`/`Application`
- Los `Controllers` solo: validan → llaman caso de uso → devuelven respuesta HTTP

### Repositorios
- Siempre definir la interfaz en `Domain` o `Application`
- Implementación en `Infrastructure` con EF Core
- No usar `DbContext` directamente en controladores ni servicios de aplicación

### DTOs
- Nunca exponer entidades de dominio directamente como respuesta de API
- Usar DTOs/ViewModels para requests y responses
- Mapear con AutoMapper o manualmente (documentar la decisión)

### JWT
- `JWT_KEY` mínimo 32 chars, leída de `IConfiguration` (nunca hardcodeada)
- Refresh token implementado y con expiración separada
- Claims mínimos en el token (no incluir datos sensibles)

### Errores
- Usar middleware global de manejo de excepciones
- Formato estándar de error:
  ```json
  {
    "error": "Descripción legible",
    "code": "ERROR_CODE",
    "details": {}
  }
  ```
- Nunca exponer stack traces en producción (`ASPNETCORE_ENVIRONMENT: Production`)

### WhatsApp / Servicios externos
- Encapsular en un servicio con su interfaz (`IWhatsAppService`)
- Manejar errores de red con reintentos (Polly o similar)
- Nunca loguear el token de WhatsApp

### Base de datos
- Filtrar siempre en SQL (LINQ que se traduzca), no en memoria
- Usar `AsNoTracking()` en queries de solo lectura
- Transacciones explícitas para operaciones que afectan múltiples tablas
- Índices en columnas usadas en `WHERE`, `JOIN` y `ORDER BY` frecuentes

### Seguridad
- Validar todos los inputs (FluentValidation o DataAnnotations)
- CORS: solo orígenes permitidos (`FRONTEND_URL`), nunca `*` en producción
- Endpoints protegidos con `[Authorize]` salvo los públicos explícitamente marcados
- No retornar IDs internos de DB innecesariamente en responses públicas