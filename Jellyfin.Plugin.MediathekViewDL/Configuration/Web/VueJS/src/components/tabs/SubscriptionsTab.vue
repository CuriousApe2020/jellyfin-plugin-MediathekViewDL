<script setup>
import { ref, onMounted } from 'vue'
import ApiService from '../../utils/ApiService'

const props = defineProps({
  onEdit: { type: Function, required: true }
})

const Dashboard = window.Dashboard ?? null

const subscriptions = ref([])
const loading = ref(false)
const error = ref(null)

async function fetchSubscriptions() {
  loading.value = true
  error.value = null
  try {
    subscriptions.value = await ApiService.getSubscriptions()
  } catch (e) {
    error.value = 'Fehler beim Laden der Abonnements.'
    console.error('Failed to fetch subscriptions', e)
  } finally {
    loading.value = false
  }
}

async function deleteSubscription(id) {
  if (!Dashboard) return
  Dashboard.confirm('Soll dieses Abonnement wirklich gelöscht werden?', 'Löschen bestätigen', async (result) => {
    if (result) {
      try {
        await ApiService.deleteSubscription(id)
        await fetchSubscriptions()
        Dashboard.alert('Abonnement gelöscht.')
      } catch (e) {
        console.error('Delete failed', e)
        Dashboard.alert('Fehler beim Löschen des Abonnements.')
      }
    }
  })
}

async function resetProcessedItems(id) {
  if (!Dashboard) return
  Dashboard.confirm('Soll der Verlauf der bereits verarbeiteten Elemente für dieses Abonnement wirklich zurückgesetzt werden?', 'Zurücksetzen bestätigen', async (result) => {
    if (result) {
      try {
        await ApiService.resetSubscriptionHistory(id)
        Dashboard.alert('Verlauf wurde zurückgesetzt.')
        await fetchSubscriptions()
      } catch (e) {
        console.error('Reset failed', e)
        Dashboard.alert('Fehler beim Zurücksetzen.')
      }
    }
  })
}

async function processSubscription(id) {
  if (!Dashboard) return
  try {
    const response = await ApiService.processSubscription(id)
    Dashboard.alert(response + ' neue Elemente gefunden.')
  } catch (e) {
    console.error('Processing failed', e)
    Dashboard.alert('Fehler beim Verarbeiten.')
  }
}

async function toggleActive(sub) {
  const newState = !sub.IsEnabled
  try {
    const result = await ApiService.setSubscriptionActive(sub.Id, newState)
    sub.IsEnabled = result === true || result === 'true'
  } catch (e) {
    console.error('Toggle failed', e)
    if (Dashboard) Dashboard.alert('Fehler beim Ändern des Status.')
  }
}

async function triggerDownloads() {
  if (!Dashboard) return
  loading.value = true
  try {
    const tasks = await ApiService.getScheduledTasks()
    const task = tasks.find(t => t.Key === 'MediathekViewDLFork-MediathekAboDownloader')

    if (!task) {
      Dashboard.alert('Scheduled Task "Mediathek Abo-Downloader" wurde nicht gefunden.')
      return
    }

    if (task.State !== 'Idle') {
      Dashboard.alert('Der Abo-Downloader läuft bereits.')
      return
    }

    await ApiService.startScheduledTask(task.Id)

    Dashboard.alert('Download-Task wurde gestartet.')
  } catch (e) {
    console.error('Failed to trigger downloads', e)
    Dashboard.alert('Fehler beim Starten des Download-Tasks.')
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  fetchSubscriptions()
})

// Expose refresh to parent if needed
defineExpose({ refresh: fetchSubscriptions })
</script>

<template>
  <div class="mvpl-card">
    <div class="header-row">
      <h2>Abo Verwaltung</h2>
      <div class="header-actions">
        <button class="btn btn-secondary" @click="triggerDownloads" :disabled="loading">Downloads manuell starten</button>
        <button class="btn btn-primary" @click="onEdit()" :disabled="loading">Neues Abo</button>
      </div>
    </div>

    <div v-if="loading" class="state-msg">
      <div class="spinner"></div>
      Lade Abonnements...
    </div>

    <div v-else-if="error" class="error-container">
      <div class="error-msg">{{ error }}</div>
      <button @click="fetchSubscriptions" class="btn btn-secondary">Erneut versuchen</button>
    </div>

    <div v-else-if="subscriptions.length > 0" class="subscriptions-list">
      <div v-for="sub in subscriptions" :key="sub.Id" class="subscription-item" :class="{ disabled: !sub.IsEnabled }">
        <div class="sub-left">
          <label class="switch" title="Abonnement aktivieren/deaktivieren">
            <input type="checkbox" :checked="sub.IsEnabled" @change="toggleActive(sub)">
            <span class="slider round"></span>
          </label>

          <div class="sub-info">
            <div class="sub-name">
              {{ sub.Name }}
            </div>
            <div class="sub-meta">
              Letzter Download: {{ sub.LastDownloadedTimestamp ? new Date(sub.LastDownloadedTimestamp).toLocaleString() : 'Nie' }}
            </div>
          </div>
        </div>
        <div class="sub-actions">
          <button @click="resetProcessedItems(sub.Id)" class="btn-icon" title="Verlauf zurücksetzen">↩️</button>
          <button @click="processSubscription(sub.Id)" class="btn-icon" title="Jetzt verarbeiten">🔄</button>
          <button @click="onEdit(sub)" class="btn-icon" title="Bearbeiten">✏️</button>
          <button @click="deleteSubscription(sub.Id)" class="btn-icon btn-delete" title="Löschen">🗑️</button>
        </div>
      </div>
    </div>

    <div v-else class="no-data">
      Keine Abonnements konfiguriert.
    </div>
  </div>
</template>

<style scoped>
.header-row { display: flex; justify-content: space-between; align-items: center; gap: 12px; margin-bottom: 20px; flex-wrap: wrap; }
.header-actions { display: flex; gap: 10px; flex-wrap: wrap; }
/* No gap: the rows sit directly on top of each other and are told apart by a separator plus
   their own padding, which reads as one list instead of a stack of floating blocks. */
.subscriptions-list { display: grid; }
.subscription-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 15px;
  border-bottom: 1px solid var(--mvpl-border, #3f3f46);
}
/* No trailing line above the card's own bottom edge. */
.subscription-item:last-child { border-bottom: none; }
/* A dashed separator instead of the solid one - the previous `border-style: dashed` drew a
   3px box around the whole row (border-width defaults to medium once a style is set), which
   now that rows carry a real border would fight with the separators. */
.subscription-item.disabled { opacity: 0.6; border-bottom-style: dashed; }
/* min-width:0 on both sides: a flex item defaults to min-width:auto, which refuses to shrink
   below its content. Without it a long subscription name pushed the row wider than the card
   instead of wrapping, and the action buttons were squeezed out of reach on a phone. */
.sub-left { display: flex; align-items: center; gap: 20px; min-width: 0; flex: 1; }
.sub-info { min-width: 0; }
.sub-name { font-weight: bold; font-size: 1.1rem; display: flex; align-items: center; gap: 10px; overflow-wrap: anywhere; }
.sub-meta { font-size: 0.85rem; color: var(--mvpl-text-secondary, #a1a1aa); margin-top: 4px; }
/* The four buttons stay on one line and keep their size - they are the row's controls, and a
   half-wrapped set of icons is harder to hit than a slightly narrower name column. */
.sub-actions { display: flex; gap: 15px; flex-shrink: 0; }
.switch { flex-shrink: 0; }

/* Phone width. The row becomes two stacked blocks - name above, controls below - because side
   by side there is not enough room left for the name to stay readable. */
@media (max-width: 600px) {
  .header-row { flex-direction: column; align-items: stretch; }
  .header-actions .btn { flex: 1; }

  .subscription-item { flex-direction: column; align-items: stretch; gap: 12px; padding: 15px 10px; }
  .sub-left { gap: 12px; }
  .sub-name { font-size: 1rem; }
  /* Aligned under the name rather than under the toggle, and spread out so the targets are
     comfortably far apart. */
  .sub-actions { justify-content: flex-end; gap: 20px; }
}
/* Slightly larger than the shared .btn-icon default (style.css) - color and hover background
   still come from there. The grayscale filter + hardcoded white text this used to have made the
   icons unreadable on light themes (white glyphs on a light background). */
.btn-icon { font-size: 1.4rem; }
.btn-delete:hover { color: #ef4444; }
.state-msg { text-align: center; padding: 40px; color: var(--mvpl-text-secondary, #a1a1aa); }
.error-container { text-align: center; padding: 30px; background: rgba(239, 68, 68, 0.1); border: 1px solid #ef4444; border-radius: 8px; color: #ef4444; }
.error-msg { margin-bottom: 10px; font-weight: bold; }
.no-data { text-align: center; color: var(--mvpl-text-secondary, #a1a1aa); padding: 40px; }

/* Switch Toggle Styles */
.switch {
  position: relative;
  display: inline-block;
  width: 44px;
  height: 24px;
}
.switch input { opacity: 0; width: 0; height: 0; }
.slider {
  position: absolute;
  cursor: pointer;
  top: 0; left: 0; right: 0; bottom: 0;
  background-color: var(--mvpl-border, #3f3f46);
  transition: .4s;
}
.slider:before {
  position: absolute;
  content: "";
  height: 18px; width: 18px;
  left: 3px; bottom: 3px;
  background-color: white;
  transition: .4s;
}
input:checked + .slider { background-color: var(--mvpl-accent, #00a4dc); }
input:focus + .slider { box-shadow: 0 0 1px var(--mvpl-accent, #00a4dc); }
input:checked + .slider:before { transform: translateX(20px); }
.slider.round { border-radius: 24px; }
.slider.round:before { border-radius: 50%; }

</style>
