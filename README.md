# Historical Timeline

A web application for creating and managing timelines of historical events, built with Blazor WebAssembly and Azure Functions.

## Overview

Historical Timeline lets you organise historical events into named timelines. Each timeline can hold any number of events, each with a title, summary, description, date range, and an optional image. Events are sorted chronologically and support dates spanning BCE and CE, from ancient history to the present day.

The application is hosted on Azure Static Web Apps with the Blazor front end served as static files and the API running as Azure Functions.

## Architecture

```
HistoricalTimeline.Client/   Blazor WebAssembly front end
HistoricalTimeline.Api/      Azure Functions HTTP API
scripts/                     Helper scripts (e.g. CSV import)
```

### API

The API is built on the [Azure Functions isolated worker model](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide) and exposes a RESTful HTTP API:

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/timelines` | List all timelines |
| POST | `/api/timelines` | Create a timeline |
| GET | `/api/timelines/{id}` | Get a timeline |
| PUT | `/api/timelines/{id}` | Update a timeline |
| DELETE | `/api/timelines/{id}` | Delete a timeline |
| GET | `/api/timelines/{id}/images/{blob}` | Download a timeline image |
| GET | `/api/timelines/{id}/historical-events` | List events for a timeline |
| POST | `/api/timelines/{id}/historical-events` | Create an event |
| GET | `/api/timelines/{id}/historical-events/{eventId}` | Get an event |
| PUT | `/api/timelines/{id}/historical-events/{eventId}` | Update an event |
| DELETE | `/api/timelines/{id}/historical-events/{eventId}` | Delete an event |
| GET | `/api/timelines/{id}/historical-events/images/{blob}` | Download an event image |

Create and update endpoints accept `multipart/form-data` requests. Date fields use `YYYY-MM-DD` for CE dates and a signed year prefix for BCE dates using astronomical year numbering, where year 0 corresponds to 1 BCE (e.g. `-1599-01-01` for 1600 BCE).

Data is stored in Azure Table Storage and images are stored in Azure Blob Storage.

### Client

The front end is a Blazor WebAssembly application that communicates with the API using an `HttpClient` whose base address is configurable via `ApiBaseUrl` in `appsettings.json`.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) (local Azure Storage emulator) or an Azure Storage account connection string
- For the import script: `bash`, `curl`, `jq`, `python3`; optionally [ImageMagick](https://imagemagick.org) for resizing large images

## Local Development

### 1. Start the storage emulator

```bash
azurite --silent
```

### 2. Configure the API

Copy `HistoricalTimeline.Api/local.settings.sample.json` to `HistoricalTimeline.Api/local.settings.json`:

```bash
cp HistoricalTimeline.Api/local.settings.sample.json HistoricalTimeline.Api/local.settings.json
```

The sample configuration connects to `UseDevelopmentStorage=true` (Azurite) by default. To use a real Azure Storage account, replace the connection string values with your own.

### 3. Run the API

```bash
cd HistoricalTimeline.Api
func start
```

The API starts on `http://localhost:7071` by default.

### 4. Run the client

```bash
cd HistoricalTimeline.Client
dotnet run
```

The client starts on `http://localhost:5256` (HTTP) and `https://localhost:7130` (HTTPS) by default.

### Build

To build the entire solution:

```bash
dotnet build HistoricalTimeline.slnx
```

## Importing Events from CSV

The `scripts/import-timeline.sh` script reads a CSV file and uploads every row as an event to a new timeline.

**Required CSV columns:** `start_date`, `end_date`, `title`, `summary`, `full_description`

**Optional CSV column:** `image_url` — an HTTP(S) URL, a `file://` URL, or a path relative to the CSV file.

```bash
./scripts/import-timeline.sh path/to/events.csv "Timeline Title" "Optional description"
```

By default the script targets the deployed API. Set `API_BASE_URL` to point at a different endpoint:

```bash
API_BASE_URL=http://localhost:7071/api ./scripts/import-timeline.sh path/to/events.csv
```

See `./scripts/import-timeline.sh --help` for full usage details.

## Deployment

The application is deployed automatically to [Azure Static Web Apps](https://learn.microsoft.com/azure/static-web-apps/overview) on every push to `main` via the GitHub Actions workflow in `.github/workflows/`. The workflow builds the Blazor client and deploys the Azure Functions API together as a single Static Web App.

The `AZURE_STATIC_WEB_APPS_API_TOKEN_WHITE_TREE_0AF7BEE10` repository secret must be set to the Static Web Apps deployment token.

## Image Support

Uploaded images must be JPEG, PNG, GIF, or WebP and must not exceed 5 MB. Images are stored privately in Azure Blob Storage and served through the API, which validates that each image belongs to the requested timeline before returning it.
