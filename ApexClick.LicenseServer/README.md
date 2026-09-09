# ApexClick.LicenseServer

.NET 9 сервис лицензий и оплаты.

## Обязательные переменные окружения
- `APEXCLICK_LICENSE_SECRET` — HMAC-секрет подписи ключей. Существует только здесь; в клиентской
  сборке `IssueKey` больше нет — клиент умеет только проверять подпись.
- `APEXCLICK_LICENSE_ADMIN_TOKEN` — токен для `/revoke`.
- `APEXCLICK_PUBLIC_BASE_URL` — публичный https-домен этого сервиса (нужен для return_url ЮKassa).

## Переменные для приёма оплаты (ЮKassa)
Пока не заданы — `/checkout/create` отвечает `503`, остальные эндпоинты (`/validate`, `/revoke`)
работают как обычно.
- `YOOKASSA_SHOP_ID`, `YOOKASSA_SECRET_KEY` — из личного кабинета yookassa.ru (появятся после
  оформления ИП/юрлица и одобрения).
- `APEXCLICK_PRICE_PRO_RUB` — цена Pro-лицензии в рублях, например `1990`.
- В кабинете ЮKassa нужно прописать вебхук на `{APEXCLICK_PUBLIC_BASE_URL}/webhooks/yookassa`,
  событие `payment.succeeded`.

## Опционально — доставка ключа на почту
- `APEXCLICK_SMTP_HOST`, `APEXCLICK_SMTP_PORT` (по умолчанию 587), `APEXCLICK_SMTP_USER`,
  `APEXCLICK_SMTP_PASSWORD`, `APEXCLICK_SMTP_FROM`.
- Без SMTP ключ всё равно доставляется — он возвращается на странице после оплаты
  (`GET /checkout/status?order=...`), просто без письма-дубликата.

## Эндпоинты
- `GET /health`
- `POST /validate` — `{ key, machineFingerprint }` → проверка подписи, отзыва, привязки к железу.
- `POST /revoke` — `{ keyId }`, требует заголовок `X-ApexClick-Admin-Token`.
- `POST /checkout/create` — `{ email, tier: "Pro" }` → `{ orderId, confirmationUrl }`, редиректить
  покупателя на `confirmationUrl`.
- `POST /webhooks/yookassa` — вызывается ЮKassa. Тело вебхука не используется как источник
  истины напрямую: сервер перепроверяет статус платежа обратным вызовом их API.
- `GET /checkout/status?order=...` — для страницы возврата: `{ status, key }`, опрашивать
  раз в 1-2 сек несколько попыток, пока `status != "Paid"` (вебхук асинхронный).

## Хранилище
- `revoked.json` / `machines.json` — как раньше, простые файлы (низкая частота записи).
- `orders.db` (SQLite) — заказы и выданные ключи. Атомарная идемпотентность на случай, если
  ЮKassa пришлёт `payment.succeeded` дважды: второй вызов ключ повторно не выпускает и письмо
  повторно не шлёт.

## Известное ограничение MVP
Если `/checkout/create` создал запись заказа, но сам запрос к ЮKassa на создание платежа упал
по сети — заказ остаётся висеть в статусе `Pending` без оплаты. Для MVP не критично (просто
"осиротевшая" запись), но при росте объёма стоит добавить фоновую очистку/повтор.
