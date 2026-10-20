# company-service

Логика работы с компаниями (C#, .NET 10): компании с группами, отрасли (разделы
ОКВЭД), участники, инвайты. Постгрес — через `AlfaHackaton.DapperContext`
(Dapper + FluentMigrator), инвайты — **MongoDB с TTL** (протухшие удаляются
Mongo автоматически) через локальный пакет `AlfaHackaton.MongoContext`
(см. `MongoContext/` — после публикации на GitHub Packages заменить
ProjectReference на PackageReference).

## Сущности

- **Company** (Postgres): id, name, INN, creator_id, parent_company_id (группа),
  region, revenue, count_of_employees, bank_profile_id (SYNTHETIC_U — мост к
  graph-service), created_at;
- **Industry** (Postgres, сид 21 раздела ОКВЭД A–U — та же разметка, что в graph-service) + m2m company_industries;
- **CompanyMember** (Postgres): кто в какой компании (без хождений в user-service);
- **InviteDocument** (Mongo `invites`): код, company_id, ExpiresAt (**TTL-индекс,
  expireAfter=0**), уникальный Code, UsedBy/UsedAt.

## Ручки (Swagger: `/company-service/swagger`)

| Метод | Путь | Доступ | Что делает |
|---|---|---|---|
| GET | `/company-service/companies/me` | JWT | своя компания + отрасли + isCreator |
| PATCH | `/company-service/companies/me` | JWT | частичное обновление (null = не трогать) |
| PUT | `/company-service/companies/me/industries` | JWT | заменить отрасли (`{"industryCodes": ["G","H"]}`) |
| GET | `/company-service/companies/me/members` | JWT | участники компании |
| GET | `/company-service/companies/me/group` | JWT | родитель + дочерние (группа) |
| GET | `/company-service/industries` | — | справочник (21 раздел ОКВЭД) |
| POST | `/company-service/companies/me/invites` | JWT, **создатель** | инвайт-код (TTL 7 дней по умолчанию, 1–30) |
| GET | `/company-service/companies/me/invites` | JWT | список со статусами active/used/expired |
| GET | `/company-service/invites/{code}` | — | публичная валидация (страница инвайта) |
| POST | `/company-service/internal/companies` | X-Internal-Secret | создать компанию при регистрации (auth-service) |
| GET | `/company-service/internal/companies/by-user/{userId}` | X-Internal-Secret | компания пользователя |
| POST | `/company-service/internal/invites/{code}/use` | X-Internal-Secret | принять инвайт + добавить участника |

Регистрация по инвайту (шов для auth-service): фронт показывает
`GET /invites/{code}` → `POST /auth/register` с кодом → auth-service вызывает
`internal/invites/{code}/use` с userId нового юзера.

## Запуск локально

Нужны Postgres и Mongo (или поднять их в Docker, см. `cluster/`):

```bash
docker run -d --name cs-postgres -e POSTGRES_DB=company-service -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres -p 5434:5432 postgres:17-alpine
docker run -d --name cs-mongo -p 27018:27017 mongo:7

cd company-service
$env:ConnectionStrings__Postgres='Host=localhost;Port=5434;Database=company-service;Username=postgres;Password=postgres'
$env:Mongo__ConnectionString='mongodb://localhost:27018'
$env:Mongo__Database='company-service'
$env:Internal__Secret='<ваш Internal:Secret из .env>'
dotnet run --urls http://127.0.0.1:8084
```

Миграции и Mongo-индексы применяются при старте автоматически.

## Проверено (смоук 19/19)

Полный сценарий на реальных миграциях: создание компании → отрасли → PATCH →
инвайты → использование коллегой → 409 на повтор → права (создатель vs коллега)
→ TTL-удаление протухшего инвайта Mongo-й через ~минуту → 401 без токена/секрета.
Индексы в Mongo: `ExpiresAt_1` (ttl=0), `Code_1` (unique), `CompanyId_1`.
