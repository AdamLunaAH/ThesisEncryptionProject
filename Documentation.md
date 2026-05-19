# SeidoSwStack Documentation

Central index for all project documentation. Each document covers a specific area of the application.

---

## Setup & Configuration

| Document                                                                  | Description                                                                                                                                                                                                   |
| ------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [Hosting Documentation](_documentation/HostingDocumentation.md)           | How to host the API and connect all components. Covers four scenarios: full localhost, LAN, WAN (port forwarding), and Azure. Includes port reference and `appsettings.json` configuration for each scenario. |
| [Create and Start Database](_documentation/CreateAndStartDatabase.md)     | Step-by-step guide for setting up the local database using EF Core migrations and User Secrets.                                                                                                               |
| [Scripts Documentation](_documentation/ScriptsDocumentation.md)           | Reference for all utility scripts in `_scripts/`. Covers `cleanup`, `install_sql_docker`, `generate-dev-certificate`, `generate-ip-dev-certificate`, `database-rebuild-all`, and `az-prep-publish`.           |
| [Technologies Documentation](_documentation/TechnologiesDocumentation.md) | Overview of the key technologies used in the project — what they are, how they work in general, and how this project uses them.                                                                               |

---

## Architecture & Features

| Document                                                               | Description                                                                                                                                                                                             |
| ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [User Documentation](_documentation/UserDocumentation.md)              | Explains the two user types (registered users and guests) and walks through the full client and backend layer stack.                                                                                    |
| [Session Documentation](_documentation/SessionDocumentation.md)        | Covers the three SignalR/Orleans session types — Chat, Game, and AppPresence — including which grains back each session and how they are managed across distributed deployments.                        |
| [Chat Documentation](_documentation/ChatDocumentation.md)              | Architecture and feature documentation for the real-time chat system, including SignalR hub setup, Orleans grain backing, and client integration.                                                       |
| [Game Documentation](_documentation/GameDocumentation.md)              | Documents the Gotoku game logic, rules, and how the game state is managed within the application.                                                                                                       |
| [Online Game Documentation](_documentation/OnlineGameDocumentation.md) | Covers the complete online multiplayer architecture: Microsoft Orleans, SignalR, JWT authorization, friend invites, matchmaking, reconnection, player statistics, and the full MAUI client integration. |

---

## API & Security

| Document                                                                  | Description                                                                                                                                         |
| ------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| [API Controller Documentation](_documentation/ControllerDocumentation.md) | Describes all controllers in `App.WebApiEncryption`, their routes, and the operations they expose.                                                  |
| [API Docs Documentation](_documentation/ApiDocsDocumentation.md)          | Explains the two interchangeable API documentation UIs (Scalar and Swagger) and how to access and configure them.                                   |
| [JWT Token Documentation](_documentation/JwtTokenDocumentation.md)        | Documents JWT bearer authentication and refresh token flow — how tokens are issued, validated, and refreshed across API endpoints and SignalR hubs. |

---

## Client

| Document                                                                 | Description                                                                                                      |
| ------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------- |
| [MAUI Application Documentation](_documentation/MauiAppDocumentation.md) | Documents the .NET MAUI client application — architecture, navigation, services, and how it connects to the API. |

---

## Other

| Document                                                  | Description                                                                                 |
| --------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| [Future Suggestions](_documentation/FutureSuggestions.md) | Backlog of remaining TODOs, planned improvements, and feature ideas for future development. |
