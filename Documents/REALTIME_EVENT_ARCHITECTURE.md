# Real-Time Event & GHG Data Notification Architecture

## 1. Context & Motivation
In **TU.ACAMP (v20)**, activity events and greenhouse gas (GHG) calculations are ingested continuously via API v2 and automated batch integrations. Users viewing the Blazor Server Web UI need immediate real-time updates without having to manually refresh or reload the page.

### Previous Issue (Antipattern)
Previously, Blazor Server components attempted to create a SignalR client (HubConnectionBuilder) connecting back to their own host (/hubs/event-notification) over HTTP loopback network. This caused:
- Blazor Server circuit breaks and HTTP 500 error on browser page refresh (F5 / SSR Prerendering).
- Socket resource waste (Server connecting back to Server).
- Thread contention and connection startup deadlocks during page initialization.

---

## 2. Hybrid Real-Time Architecture Design

To achieve zero-overhead real-time performance and 100% page load stability, ACAMP adopts a **Hybrid Notification Pattern**:

`mermaid
flowchart TD
    API[API v2 Ingest / User Action] --> Svc[EventIngestionService]
    
    subgraph Internal [Internal Server Process (Blazor UI)]
        Svc -->|In-Memory Direct Publish| Bus[GhgEventBusService (Singleton)]
        Bus -->|C# Event Subscription| TopicPage[PageListActivityTopic]
        Bus -->|C# Event Subscription| EventPage[PageListActivityEvent]
        Bus -->|C# Event Subscription| Dashboard[Dashboard & Other Pages]
        TopicPage -->|Native Blazor Circuit| BrowserUI[User Browser UI]
    end

    subgraph External [External Clients & 3rd Party]
        Svc -->|WebSocket Broadcast| Hub[EventNotificationHub (SignalR)]
        Hub -->|SignalR Client Protocol| Mobile[Mobile App / External Dashboards]
    end
`

### Key Components:
1. **GhgEventBusService (Singleton In-Memory Event Bus)**:
   - Resides in server memory (AppDomain).
   - Dispatches typed notifications (GhgEventIngestedNotification) to all active Blazor circuits with 0ms latency and 0 network overhead.
   - Any Blazor page can inject GhgEventBusService, subscribe via OnEventIngested += Handler on OnInitialized, and unsubscribe on DisposeAsync.

2. **EventNotificationHub (SignalR Server Hub)**:
   - Retained on the server for external consumers (e.g., external mobile apps, third-party monitoring clients).
   - EventIngestionService publishes to both GhgEventBusService and EventNotificationHub simultaneously.

---

## 3. UI Component Lifecycle Rules
- **No HubConnection inside Blazor Server components**: Internal components must always use GhgEventBusService.
- **No NavigateTo in Component Property Setters**: URL query state synchronization must be triggered only by explicit user click actions (OnTabChanged, OnGridPageChanged), never inside property getters/setters or during render/prerender cycles.
