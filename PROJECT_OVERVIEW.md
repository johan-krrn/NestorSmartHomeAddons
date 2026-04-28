# PROJECT_OVERVIEW.md — Nestor Smart Home Addons

> Généré le 27 avril 2026 — Analyse complète du dépôt.

---

## 1. Résumé du Projet

**Nestor Smart Home Addons** est un dépôt de deux add-ons Home Assistant (HAOS) qui relient un cloud Azure Nestor à une instance Home Assistant locale. Il offre un pont bidirectionnel MQTT v5 ↔ WebSocket HA ainsi qu'un mécanisme de provisionnement automatique de certificats X.509 pour l'authentification mTLS.

---

## 2. Stack Technique

| Couche | Technologie |
|---|---|
| Langage principal (bridge) | C# 12 / .NET 8 |
| Framework web | ASP.NET Core Minimal API |
| Protocole cloud | MQTT v5 via [Azure Event Grid MQTT Broker](https://learn.microsoft.com/azure/event-grid/mqtt-overview) |
| Bibliothèque MQTT | MQTTnet 4.3.7 |
| Protocole HA | Home Assistant WebSocket API |
| Tests | xUnit 2.7 + NSubstitute 5.1 |
| Conteneurisation | Docker (multi-stage, multi-arch : `amd64` / `aarch64`) |
| Image de base HA | `ghcr.io/home-assistant/{arch}-base-debian:bookworm` |
| Supervision de processus | s6-overlay (fourni par l'image de base HA) |
| Langage provisionnement | Bash (Alpine Linux 3.19) |
| Outils provisionnement | `openssl`, `curl`, `jq` |
| Sécurité | TLS 1.2+, SAS token ou X.509 mTLS |

---

## 3. Architecture & Structure des Dossiers

```
NestorSmartHomeAddons/
├── repository.yaml                      # Déclaration du dépôt d'add-ons HA (nom, URL, mainteneur)
│
├── nestor_smart_home_bridge/            # Add-on 1 — Pont MQTT ↔ HA WebSocket
│   ├── config.yaml                      # Manifeste HA : version 1.1.2, schéma des options, ingress
│   ├── build.yaml                       # Images de base par architecture, labels OCI
│   ├── Dockerfile                       # Build multi-stage : SDK .NET 8 → runtime HA Debian bookworm
│   ├── DOCS.md                          # Documentation utilisateur (FR)
│   ├── rootfs/etc/services.d/
│   │   └── nestor-bridge/
│   │       ├── run                      # Script s6 : lance `dotnet NestorBridge.dll`
│   │       └── finish                   # Script s6 : arrête la supervision à la sortie du processus
│   └── src/
│       ├── NestorBridge/                # Application principale
│       │   ├── Program.cs               # Point d'entrée, DI, routes API, BootstrapService (inline)
│       │   ├── Configuration/
│       │   │   ├── BridgeOptions.cs     # POCO des options + validation (mqtt_host, box_id, auth…)
│       │   │   └── OptionsJsonLoader.cs # Charge /data/options.json injecté par HA Supervisor
│       │   ├── HomeAssistant/
│       │   │   ├── HaWebSocketClient.cs # Client WS HA : auth, subscribe state_changed, call_service
│       │   │   ├── HaServiceCaller.cs   # API haut niveau : ExecuteCommandAsync, PublishMqttAsync
│       │   │   ├── IHaWebSocketClient.cs
│       │   │   └── Models/
│       │   │       ├── CloudPayloads.cs # CloudCommand, CommandAck, TelemetryPayload, CloudRequest
│       │   │       ├── HaMessages.cs    # HaEvent, HaEventData, HaState, HaMessage
│       │   │       └── HaCallServiceMessage.cs
│       │   ├── Mqtt/
│       │   │   ├── MqttBridge.cs        # Client MQTTnet v5 : connexion, pub/sub, reconnexion auto
│       │   │   ├── IMqttBridge.cs
│       │   │   └── Topics.cs            # Helpers de construction des topics MQTT Nestor
│       │   ├── Services/
│       │   │   ├── DownlinkWorker.cs    # Cloud → HA : reçoit les commandes MQTT, appelle HA
│       │   │   ├── UplinkWorker.cs      # HA → Cloud : publie la télémétrie et les événements bruts
│       │   │   └── HeartbeatWorker.cs   # Publie un heartbeat toutes les 60 secondes
│       │   ├── Translation/
│       │   │   ├── CommandTranslator.cs # Désérialise les payloads MQTT → CloudCommand + buildAck
│       │   │   └── TelemetryTranslator.cs # HaEvent → payload de télémétrie filtré par domaine
│       │   └── Web/
│       │       ├── MessageLog.cs        # Ring buffer thread-safe (500 entrées) + diffusion SSE
│       │       └── wwwroot/index.html   # Dashboard de monitoring (dark theme, SSE live feed)
│       └── NestorBridge.Tests/          # Suite de tests unitaires
│           ├── BridgeOptionsTests.cs
│           ├── CommandTranslatorTests.cs
│           ├── TelemetryTranslatorTests.cs
│           └── TopicsTests.cs
│
└── nestor_smart_home_provisioning/      # Add-on 2 — Provisionnement X.509
    ├── config.json                      # Manifeste HA : version 1.0.3, schéma des options
    ├── Dockerfile                       # Image Alpine 3.19 + bash/curl/openssl/jq
    ├── run.sh                           # Point d'entrée : enrôlement initial + boucle de renouvellement
    ├── enroll.sh                        # Génère clé RSA 2048, CSR, appelle POST /api/enroll
    └── renew.sh                         # Vérifie l'expiration et renouvelle via POST /api/renew (mTLS)
```

### Convention des topics MQTT

| Direction | Topic | Usage |
|---|---|---|
| Souscription | `devices/{boxId}/commands/#` | Réception de toutes les commandes cloud |
| Publication | `devices/{boxId}/commands/{commandId}/ack` | Accusé de réception de commande |
| Publication | `devices/{boxId}/telemetry/state/{entityId}` | Télémétrie filtrée par domaine |
| Publication | `devices/{boxId}/events/state_changed` | Événements bruts `state_changed` |
| Publication | `devices/{boxId}/heartbeat` | Heartbeat toutes les 60 s |
| Souscription | `devices/{boxId}/commands/requests` | Requêtes cloud (ex : `get_states`) |
| Publication | `devices/{boxId}/responses` | Réponses aux requêtes cloud |

---

## 4. Fonctionnalités Implémentées

### Add-on : `nestor_smart_home_bridge` (v1.1.2)

- **Connexion MQTT v5** vers Azure Event Grid avec authentification **SAS** (username/password) ou **X.509 mTLS** (cert + clé)
- **Connexion WebSocket HA** avec authentification via `SUPERVISOR_TOKEN` (ou `HA_TOKEN` en développement local), abonnement aux événements `state_changed`
- **Downlink (Cloud → HA)** : réception de commandes MQTT, traduction en `call_service` HA, renvoi d'un ack `success`/`error`
- **Uplink (HA → Cloud)** :
  - Publication de télémétrie filtrée (`devices/{boxId}/telemetry/state/{entityId}`) selon les domaines configurés (`light`, `switch`, `sensor`, `climate`, `binary_sensor` par défaut)
  - Publication d'événements bruts `state_changed` en temps réel
- **Passthrough MQTT** : les commandes sur des sous-topics non réservés sont retransmises vers le broker MQTT local HA via `mqtt.publish`
- **Requêtes cloud** : gestion des requêtes `get_states` sur `devices/{boxId}/commands/requests` avec réponse sur `devices/{boxId}/responses`
- **Heartbeat** : publication d'un payload `{boxId, timestamp, status:"alive"}` toutes les 60 secondes
- **Reconnexion automatique** : backoff exponentiel (1 s → 60 s max) pour MQTT et WebSocket HA
- **Dashboard web** : interface de monitoring sur le port ingress 8099 avec historique de 200 messages et flux SSE en temps réel
- **Validation de configuration** : échec immédiat au démarrage si `mqtt_host`, `box_id` ou `mqtt_client_id` sont absents
- **Logging structuré JSON** avec niveau configurable (`trace` | `debug` | `info` | `warning` | `error`)
- **Option `no_tls`** : désactivation du TLS pour les tests locaux contre un broker Mosquitto

### Add-on : `nestor_smart_home_provisioning` (v1.0.3)

- **Enrôlement initial** : génération d'une clé RSA 2048, d'une CSR et appel à `POST /api/enroll` pour obtenir le certificat device et le CA
- **Renouvellement automatique** : vérification de l'expiration toutes les 30 minutes et renouvellement via `POST /api/renew` (authentification mTLS)
- **Remplacement atomique** du certificat renouvelé (`mv device.crt.new device.crt`)
- **Fallback hostname** : si `affaire` n'est pas défini dans la configuration, utilise `$(hostname)`
- Montage en lecture/écriture du répertoire `/ssl` (partagé avec le bridge)

---

## 5. État de l'Infrastructure

| Élément | Statut | Détail |
|---|---|---|
| Docker multi-stage | ✅ Présent | Build SDK .NET 8 → runtime HA Debian bookworm |
| Multi-architecture | ✅ Présent | `amd64` et `aarch64` |
| s6-overlay | ✅ Présent | Scripts `run` et `finish` dans `rootfs/` |
| HA Ingress | ✅ Configuré | Port 8099, icône `mdi:bridge`, titre panel |
| HA APIs activées | ✅ Configuré | `hassio_api`, `homeassistant_api`, `auth_api` |
| CI/CD | ❌ Absent | Aucun fichier `.github/workflows/` ou pipeline équivalent |
| Tests automatisés | ✅ Présent | xUnit, exécutables localement via `dotnet test` |
| Manifeste dépôt HA | ✅ Présent | `repository.yaml` à la racine |
| Secrets / credentials | ✅ Sécurisé | Aucun credential hardcodé — tout via `options.json` ou variables d'environnement |

---

## 6. Dette Technique & Points d'Attention

### Anomalies identifiées

| Fichier | Type | Description |
|---|---|---|
| `nestor_smart_home_provisioning/renew.sh` | **Bug potentiel** | Le seuil d'expiration est actuellement `21600 s` **(6 heures)** — la valeur de production commentée (`864000 s` = 10 jours) est désactivée. Commentaire explicite `# 6 heures pour test`. Ce paramètre doit être restauré pour la production. |
| `nestor_smart_home_bridge/src/NestorBridge/Configuration/OptionsJsonLoader.cs` | **Silence à confirmer** | Si `/data/options.json` est absent, la méthode retourne silencieusement sans lever d'exception (le `File.Exists` ignore le fichier). La validation de `BridgeOptions.Validate()` rattrapera les champs obligatoires, mais les champs optionnels seront silencieusement à leur valeur par défaut. |
| `nestor_smart_home_bridge/src/NestorBridge/HomeAssistant/HaWebSocketClient.cs` | **À confirmer** | La reconnexion WebSocket est implémentée, mais la gestion du cycle de vie du `_reconnectTask` (champ `Task _reconnectTask = Task.CompletedTask`) mérite une revue pour s'assurer qu'une reconnexion concurrente ne peut pas être déclenchée deux fois simultanément. |
| Aucun fichier CI/CD | **Absence** | Pas de pipeline de build/test automatisé (GitHub Actions ou autre). La construction des images Docker et l'exécution des tests unitaires sont manuelles. |
| `NestorBridge.Tests/GlobalUsings.cs` | **Fichier minimal** | Contient uniquement les `global using` — pas de problème fonctionnel, mais confirme que la couverture de test est limitée aux composants de traduction et de configuration (les workers, le client WebSocket et le bridge MQTT ne sont pas testés). |
| Domaines de télémétrie codés dans `config.yaml` | **Flexibilité limitée** | La liste par défaut (`light`, `switch`, `sensor`, `climate`, `binary_sensor`) est satisfaisante pour un usage courant, mais des domaines comme `cover`, `fan` ou `media_player` ne sont pas inclus par défaut. |

### Fonctionnalités non couvertes par les tests

- `MqttBridge` (connexion, reconnexion, pub/sub)
- `HaWebSocketClient` (authentification WebSocket, réception d'événements)
- `DownlinkWorker` / `UplinkWorker` / `HeartbeatWorker`
- `HaServiceCaller`
- `MessageLog` (SSE)
