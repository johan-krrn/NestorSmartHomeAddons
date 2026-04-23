# Nestor Bridge — Documentation

## Fonctionnement

Nestor Bridge est un add-on Home Assistant qui agit comme pont bidirectionnel entre le cloud Azure Nestor et le core Home Assistant local.

### Downlink (Cloud → HA)

1. L'add-on s'abonne au topic MQTT `devices/{boxId}/commands/#` sur Azure Event Grid
2. Chaque commande reçue est désérialisée et traduite en appel `call_service` via le WebSocket HA
3. Un accusé de réception (ack) est publié sur `devices/{boxId}/commands/{commandId}/ack`

### Uplink (HA → Cloud)

1. L'add-on écoute les événements `state_changed` via le WebSocket HA
2. Les entités sont filtrées par domaine (configurable)
3. Les changements d'état sont publiés sur `devices/{boxId}/telemetry/state/{entityId}`

### Heartbeat

Un message de heartbeat est publié toutes les 60 secondes sur `devices/{boxId}/heartbeat`.

## Résilience

- **Reconnexion automatique** : en cas de perte de connexion MQTT ou WebSocket, l'add-on tente de se reconnecter avec un backoff exponentiel (1s → 2s → 4s → ... → 60s max)
- **Commandes malformées** : un ack `status=error` est renvoyé au cloud avec le détail de l'erreur
- **Erreurs HA** : les erreurs de `call_service` sont capturées et remontées dans l'ack

## Sécurité

- Authentification MQTT via SAS token ou certificat X.509 (mTLS)
- TLS 1.2+ obligatoire pour la connexion Event Grid
- Le token Supervisor est injecté automatiquement par HAOS (variable `SUPERVISOR_TOKEN`)
- Aucun credential n'est hardcodé — tout passe par `options.json`
