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
| POST | `/company-service/companies` | JWT | создать свою компанию (409 — юзер уже состоит); создатель становится участником |
| GET | `/company-service/companies/me` | JWT | своя компания + отрасли + isCreator |
| PATCH | `/company-service/companies/me` | JWT | частичное обновление (null = не трогать) |
| PUT | `/company-service/companies/me/industries` | JWT | заменить отрасли (`{"industryCodes": ["G","H"]}`) |
| GET | `/company-service/companies/me/members` | JWT | участники компании |
| GET | `/company-service/companies/me/group` | JWT | родитель + дочерние (группа) |
| GET | `/company-service/industries` | — | справочник (21 раздел ОКВЭД) |
| POST | `/company-service/companies/me/invites` | JWT, **создатель** | инвайт-код (TTL 7 дней по умолчанию, 1–30) |
| GET | `/company-service/companies/me/invites` | JWT | список со статусами active/used/expired |
| GET | `/company-service/invites/{code}` | — | публичная валидация (страница инвайта) |
| POST | `/company-service/internal/companies` | X-Internal-Secret | создать компанию при регистрации (auth-service); 409 — создатель уже состоит |
| GET | `/company-service/internal/invites/{code}` | X-Internal-Secret | валидация кода инвайта (auth-service перед регистрацией по ссылке) |
| GET | `/company-service/internal/companies/by-user/{userId}` | X-Internal-Secret | компания пользователя |
| GET | `/company-service/internal/companies/{companyId}/member-ids` | X-Internal-Secret | id всех участников компании |
| POST | `/company-service/internal/invites/{code}/use` | X-Internal-Secret | принять инвайт + добавить участника + **удалить инвайт**; 409 — юзер уже в компании |

Регистрация по инвайту (шов для auth-service): фронт показывает
`GET /invites/{code}` → `POST /auth-service/register-invite` с кодом →
auth-service вызывает `internal/invites/{code}/use` с userId нового юзера;
инвайт-запись при успехе **удаляется** из Mongo, юзер сразу в компании.

Операционное членство допускает одну компанию на пользователя. Это правило
защищено `UNIQUE (user_id)`: создание компании и членство создателя записываются
одной транзакцией. При принятии инвайта членство резервируется до его атомарного
использования; проигравший конкурентный запрос получает 409 и не использует
другой инвайт. Версионные роли и группы анализа этим ограничением не меняются.
Миграция `20261024002` останавливается при существующих конфликтующих членствах,
сохраняя все строки; перед повторным запуском требуется явно разрешить конфликт.

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

HTTP-регрессии членства и проверки knowledge API запускаются на изолированном
PostgreSQL с правами создавать и удалять тестовые базы:

```bash
export COMPANY_TEST_POSTGRES='Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Pooling=false'
dotnet test Tests/CompanyService.Tests.csproj -c Release
```

Нужны .NET SDK 10.0.x и доступ к опубликованным пакетам организации из GitHub
Packages. Тесты членства используют настоящий PostgreSQL и атомарный тестовый
репозиторий инвайтов; MongoDB для них не требуется.

## Проверено (смоук 19/19)

Полный сценарий на реальных миграциях: создание компании → отрасли → PATCH →
инвайты → использование коллегой → 409 на повтор → права (создатель vs коллега)
→ TTL-удаление протухшего инвайта Mongo-й через ~минуту → 401 без токена/секрета.
Индексы в Mongo: `ExpiresAt_1` (ttl=0), `Code_1` (unique), `CompanyId_1`.

## Версионные профили и инструменты анализа

`/company-service/internal/knowledge` — авторитетный API профилей компаний,
явных групп, связей людей и ролевых разрешений. Все маршруты требуют
`X-Internal-Secret` (`Internal:Secret`). Профили хранятся в PostgreSQL отдельно
от операционных таблиц компаний: ревизия неизменяема, `knownAt` задаёт срез
знаний, источник фиксируется тройкой materialId/versionId/contentHash.
Из связи человека с компанией и членства в группе права чтения не выводятся.

- `POST/GET companies`, `POST/GET groups`, `POST/GET roles`, `POST/GET people`:
  запись ревизии и чтение последней известной на `?asOf=...`.
- `GET companies/versions`: все ревизии для перестраиваемой поисковой проекции.
- `GET groups/{id}?asOf=...`: раскрытие вложенных групп с путями, пропусками и циклами.
- `GET companies/{id}?asOf=...`, `GET people/{id}?asOf=...`,
  `GET role-scope?personId=...&companyId=...&asOf=...`:
  профиль человека и явное ролевое разрешение (404 при отсутствии).
- `GET changes?after=0`: последовательные изменения, опубликованные в одной
  транзакции с профилем; курсор принадлежит CompanyService.
- `POST selection`: `{asOf,targetCompanyIds,groupIds,personIds,queries,
  previousCompanyIds,companyBudget,projectionOnly}` →
  `{groups,selectedCompanyIds,pendingCompanyIds}`. CompanyService ранжирует
  найденные ContextService компании через RRF, раскрывает группы и применяет
  явные ролевые разрешения. Явные targetCompanyIds имеют приоритет.
- `POST segments`: `{asOf,companyIds,granularity:"activity"}` → `{segments}`;
  группировка по полному ОКВЭД, `broad` — по первым двум цифрам.
- `POST role-scope`: `{asOf,personIds,companyIds}` →
  `{roles,missingPersonIds,companyIds}`. Возвращает явные роли и объединение
  запрошенных компаний с разрешёнными; отсутствующие роли видны отдельно.
  При пустом `personIds` выбираются явные роли с разрешением на запрошенные компании.

`ContextService:BaseUrl` — корень HTTP сервиса материалов; для проверки
неизменяемых источников и поиска используется
`ContextService:InternalSecret` с fallback на `Internal:Secret`.
CompanyService проверяет hash, время знания и компанию источника перед записью.
При недоступности ContextService запись завершается ошибкой, локального
fallback на его прежнее хранилище профилей нет.

Миграция создаёт `company_knowledge_revisions` и `company_knowledge_changes`.
Если профили уже записывались в прежний ContextService, перед переключением
нужно явно перенести их через этот API с исходными ревизиями и источниками;
автоматического межсервисного копирования или удаления данных нет.

## Public company knowledge

Five current-company profile/material operations, creator-only writes, durable idempotency receipts and native gates are documented in [docs/public-company-knowledge.md](docs/public-company-knowledge.md). This source change is not a deployment; the internal Context list dependency must be deployed separately first.
