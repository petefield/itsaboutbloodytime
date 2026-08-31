# Copilot Agent Configuration

This document describes how GitHub Copilot coding agent is configured for this repository.

## Setup Steps

The agent's cloud environment is configured by `.github/copilot-setup-steps.yml` when present. For this repository no custom setup steps are defined, so the agent uses the default environment provided by GitHub.

## What the Agent Can Do

The Copilot coding agent can:

- **Add features** — implement new API endpoints, Blazor components, or data-model changes.
- **Fix bugs** — diagnose and resolve issues in the API, the client, or the import script.
- **Write and update documentation** — create or revise Markdown files such as this one.
- **Refactor code** — improve structure, naming, or performance while preserving behaviour.
- **Run the build** — validate changes with `dotnet build HistoricalTimeline.slnx`.

## Build and Validation

The repository is validated by building the solution:

```bash
dotnet build HistoricalTimeline.slnx
```

There are no automated test projects in this repository. The agent performs build validation after every code change to confirm the solution compiles without errors.

## Deployment

Deployment is handled by the GitHub Actions workflow in `.github/workflows/azure-static-web-apps-white-tree-0af7bee10.yml`. Pushes to `main` trigger an automatic build and deployment to Azure Static Web Apps. The agent does not push directly to `main`; it works on feature branches and opens pull requests.

## Scope and Limitations

- The agent works within the `petefield/itsaboutbloodytime` repository only.
- It does not have access to Azure subscription credentials or storage account keys, so it cannot run the application or perform end-to-end testing against a live environment.
- Secrets such as `AZURE_STATIC_WEB_APPS_API_TOKEN_WHITE_TREE_0AF7BEE10` are managed in repository settings and are not accessible to the agent.
