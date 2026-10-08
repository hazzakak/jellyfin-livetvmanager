# Jellyfin Live TV Manager & Cleaner Plugin

[![Repository](https://img.shields.io/badge/GitHub-hazzakak%2Fjellyfin--livetvmanager-blue?logo=github)](https://github.com/hazzakak/jellyfin-livetvmanager)

A specialized Jellyfin plugin designed to solve the persistent issue where deleted or modified Live TV tuners leave behind orphaned channels and guide data with no native way to delete them.

---

## 🧩 The Problem in Jellyfin

1. **Channels Cannot Be Deleted by Design in Core Jellyfin**:
   In Jellyfin's core codebase, `LiveTvChannel.CanDelete()` is hardcoded to return `false`. As a result, the web UI never shows a "Delete" button for channels, and calling `DELETE /Items/{channelId}` via the REST API returns `401 Unauthorized`.
2. **Fragile Cleanup on Tuner Removal**:
   When you remove a tuner (e.g., M3U playlist or HDHomeRun), Jellyfin removes the tuner configuration from `livetv.xml` and queues a guide refresh. However, inside `GuideManager`, database cleanup only runs if **every** channel and guide source reports zero errors (`cleanDatabase &= !hasErrors`). If a single channel, stream, or XMLTV URL times out or fails (very common with IPTV lists), database cleanup is aborted and the orphaned channels remain in the database permanently.
3. **No Standalone Channel Clean or Reset Option**:
   Jellyfin lacks any administrative action to simply wipe stale Live TV channels without wiping the entire server database.

---

## ✨ Features

- **Dashboard Configuration Page (Native Web UI)**:
  - **Direct Sidebar Link**: Appears directly in the Jellyfin Dashboard sidebar navigation under the **Plugins** section (and under **Live TV**) as **Live TV Cleaner** with a dedicated icon — no need to navigate through Plugins > Card > Settings!
  - **Live Metric Cards**: Total channels, active channels, orphaned channels, guide programs, and configured tuners.
  - **🧹 Clean Orphaned Channels**: Automatically detects channels that no longer match any active tuner and safely removes them and their associated programs.
  - **⚠️ Reset All Live TV Channels**: Completely purges all Live TV channels and guide programs from the database to give you a clean slate for re-importing.
  - **📅 Purge Guide Data Only**: Wipes stale EPG guide programs without touching channels.
  - **Channels Table & Single/Bulk Delete**: Browse all channels with status badges (`Orphaned` vs `Active`), filter by status or search by name/number, and delete individual or selected channels directly.
  - **🔄 Refresh Guide & Tuners**: Trigger an immediate guide and tuner rescan directly from the dashboard.
- **Scheduled Tasks**:
  - **Clean Orphaned Live TV Channels**: Runnable manually or scheduled (e.g., daily) from **Dashboard → Scheduled Tasks**.
  - **Reset All Live TV Channels**: Runnable from Scheduled Tasks as a one-click manual task.
- **REST API Endpoints** (Administrator only):
  - `GET /LiveTvCleaner/Status` — Returns overview metrics and configured tuners.
  - `GET /LiveTvCleaner/Channels` — Returns all channels with their orphan status.
  - `DELETE /LiveTvCleaner/Channels/{id}` — Force-deletes a specific channel and its programs.
  - `POST /LiveTvCleaner/DeleteSelected` — Bulk deletes selected channels by ID.
  - `POST /LiveTvCleaner/DeleteOrphaned` — Cleans all orphaned channels.
  - `POST /LiveTvCleaner/DeleteAllChannels` — Wipes all channels and guide programs.
  - `POST /LiveTvCleaner/DeleteAllPrograms` — Wipes all guide programs.
  - `POST /LiveTvCleaner/RefreshGuide` — Triggers a guide refresh.

---

## 🚀 Installation & Seamless Updates

### Option 1: Add as a Plugin Repository (Recommended — One-Click Install & Auto-Updates)

You can add this repository directly into Jellyfin so it shows up in your Catalog and updates seamlessly whenever a new version is released:

1. Open your Jellyfin Web UI.
2. Navigate to **Dashboard → Plugins → Repositories** tab.
3. Click the **"+" (Add)** button.
4. Enter:
   - **Repository Name**: `Live TV Manager`
   - **Repository URL**: `https://raw.githubusercontent.com/hazzakak/jellyfin-livetvmanager/main/manifest.json`
5. Click **Save**.
6. Switch to the **Catalog** tab under Plugins.
7. Locate **Live TV Cleaner**, click it, and hit **Install**!
8. Restart Jellyfin when prompted.

> 💡 **Seamless Updates**: When an update is published to this GitHub repository, Jellyfin's automated update checker will notify you in **Dashboard → Plugins → Catalog** with an **Update** button.

---

### Option 2: Manual DLL Copy (Proxmox / Docker / Linux)

1. Build the plugin (Release):
   ```bash
   dotnet build -c Release
   ```
   The compiled assembly will be at:
   `bin/Release/net9.0/Jellyfin.Plugin.LiveTvCleaner.dll`

2. Locate your Jellyfin `plugins` directory:
   - **Docker / Proxmox LXC Container**: typically `/config/plugins` or `/var/lib/jellyfin/plugins` (or mapped volume on your host, e.g. `A:/` or similar mount).
   - **Debian / Ubuntu bare-metal**: `/var/lib/jellyfin/plugins`
   - **Windows**: `%ProgramData%\Jellyfin\Server\plugins`

3. Create a folder named `LiveTvCleaner` inside `plugins`:
   ```bash
   mkdir -p /var/lib/jellyfin/plugins/LiveTvCleaner
   ```

4. Copy `Jellyfin.Plugin.LiveTvCleaner.dll` into that folder:
   ```bash
   cp Jellyfin.Plugin.LiveTvCleaner.dll /var/lib/jellyfin/plugins/LiveTvCleaner/
   ```

5. Restart the Jellyfin server / container:
   ```bash
   systemctl restart jellyfin
   # or for docker:
   docker restart jellyfin
   ```

6. Open the Jellyfin Web UI, navigate to **Dashboard → Plugins**, and you will see **Live TV Cleaner** listed! Click on it to open the management page.

---

## 🛠️ Usage Guide

### 1. Cleaning Orphaned Channels
1. Open Jellyfin **Dashboard → Plugins → Live TV Cleaner**.
2. Look at the **Orphaned Channels** metric card.
3. Click **Clean Orphaned Channels**.
4. Confirm the prompt. The plugin will delete all channels whose tuners have been deleted, along with their associated guide programs.

### 2. Full Live TV Reset
1. If your Live TV database is cluttered with broken channels or duplicate M3U entries:
2. Click **Reset All Live TV Channels**.
3. Confirm the prompt. All channels and programs will be wiped cleanly.
4. Go to **Dashboard → Live TV → Tuner Devices** and click **Refresh Guide Data** to re-import fresh channels.

### 3. Deleting Specific Channels
1. In the **Channels Table** on the plugin page, use the search box or filter dropdown (`Orphaned Only`, `Active Only`, `All Channels`).
2. Click **Delete** next to any specific channel to force-delete it immediately.
3. Or check the boxes next to multiple channels and click **Delete Selected**.

### 4. Running via Scheduled Tasks
- Go to **Dashboard → Scheduled Tasks**.
- Under the **Live TV** section, you will find:
  - `Clean Orphaned Live TV Channels`
  - `Reset All Live TV Channels`
- You can click the Play icon to run either task on demand, or configure an automated trigger (e.g. daily at 3:00 AM).

---

## 🔒 Security
All API endpoints and cleanup actions are secured with Jellyfin's `RequiresElevation` policy, meaning only users with Server Administrator rights can view or execute actions.
