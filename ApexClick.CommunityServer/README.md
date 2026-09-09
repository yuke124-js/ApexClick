# ApexClick.CommunityServer

ASP.NET Core .NET 9 minimal API for moderated ApexClick scripts.

Endpoints: `/health`, `/publish`, `/search`, `/download/{id}`, `/moderation/{id}/approve`, `/moderation/{id}/reject`.

Publishing writes to an app-data store and defaults to pending moderation. The deterministic scanner rejects oversized scripts and execution-like payloads. This is the server-side enforcement layer; a real production deployment should replace the JSON file store with a database and add authentication/rate limiting.
