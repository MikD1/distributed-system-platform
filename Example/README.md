# Distributed system example

Demonstrates basic service-to-service communication with observability.

- ServiceA
  - Port: 10001
  - API
    - POST: /api/message-a
    - POST: /api/error
  - Swagger: http://localhost:10001/swagger/index.html
- ServiceB
  - Port: 10002
  - API
    - POST: /api/message-b

```mermaid
flowchart LR
    subgraph Platform
        TrafficGenerator[Traffic Generator]
        Prometheus[(Prometheus)]
        Grafana[Grafana]
    end
    subgraph System
        ServiceA[ServiceA]
        ServiceB[ServiceB]
    end

    TrafficGenerator -->|/api/error| ServiceA

    TrafficGenerator -->|/api/message-a| ServiceA
    ServiceA -->|/api/message-b| ServiceB

    ServiceA -.->|metrics| Prometheus
    ServiceB -.->|metrics| Prometheus
    Grafana --> Prometheus
```