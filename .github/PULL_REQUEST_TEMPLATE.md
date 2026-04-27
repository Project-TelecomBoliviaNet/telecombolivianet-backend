## 🚀 Pull Request — TelecomBoliviaNet Backend

### 📝 Descripción
<!-- Describe claramente qué hace este PR y por qué es necesario -->

Este PR implementa la funcionalidad de **[describir aquí]** en el sistema de gestión TelecomBoliviaNet.

### 🔗 User Story relacionada
<!-- Ejemplo: US-01 · Autenticación JWT -->
**US-XX · [Nombre de la User Story]**

### ✏️ Cambios realizados

**Endpoint(s) nuevo(s):**
- `MÉTODO /api/ruta` — descripción

**Validaciones de negocio:**
- [ ] Validación 1
- [ ] Validación 2

**Respuestas de la API:**
- `200 OK` — éxito con datos
- `201 Created` — recurso creado
- `204 No Content` — éxito sin cuerpo
- `400 Bad Request` — error de validación
- `401 Unauthorized` — no autenticado
- `403 Forbidden` — sin permisos
- `404 Not Found` — recurso no encontrado

### 🔍 Tipo de cambio
- [ ] 🐛 Bug fix (corrección que no rompe funcionalidad existente)
- [ ] ✨ Nueva feature (funcionalidad nueva sin romper lo existente)
- [ ] 💥 Breaking change (cambio que afecta funcionalidad existente)
- [ ] ♻️ Refactor (mejora de código sin cambiar comportamiento)
- [ ] 📚 Documentación

### 🧪 ¿Cómo fue probado?

**Usando Swagger / Postman:**
1. Autenticarse como Admin o rol correspondiente
2. Ejecutar `MÉTODO /api/ruta`
3. Verificar respuesta esperada

**Escenarios probados:**
- ✅ Caso exitoso: [descripción]
- ❌ Caso de error: [descripción]
- ❌ Sin autorización: devuelve 401/403

### ✅ Checklist
- [ ] 🔎 Realicé auto-revisión del código
- [ ] 🚫 No se generan nuevas advertencias del compilador
- [ ] 🔒 No se exponen datos sensibles en las respuestas
- [ ] 📋 Se respeta la política de autorización correcta
- [ ] 🧹 No hay código comentado ni debug logs
- [ ] 🏗️ Se respeta la arquitectura Clean Architecture (Domain → Application → Infrastructure → Presentation)
