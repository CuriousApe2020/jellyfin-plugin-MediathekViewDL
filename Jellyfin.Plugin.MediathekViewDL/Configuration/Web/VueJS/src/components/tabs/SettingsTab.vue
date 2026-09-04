<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import ApiService from "../../utils/ApiService.js";

const Dashboard = window.Dashboard ?? null
const PLUGIN_ID = 'b24a1e41-befb-455c-8417-69b89f25c335'
const PLUGIN_NAME = 'MediathekViewDLFork'

const emit = defineEmits(['config-saved'])

// --- State ---
const loading = ref(false)
const saving = ref(false)
const lastRun = ref(null)

// Paths
const useTopicForMoviePath = ref(false)
const defaultDownloadPath = ref('')
const subscriptionShowPath = ref('')
const subscriptionMoviePath = ref('')
const manualShowPath = ref('')
const manualMoviePath = ref('')
const tempDownloadPath = ref('')

// Download
const downloadSubtitles = ref(false)
const readRate = ref(0)
const scanLibraryAfterDownload = ref(true)
const minFreeDiskSpaceMiB = ref('')

// Search
const fetchStreamSizes = ref(false)
const searchInFutureBroadcasts = ref(false)
const searchPageSize = ref(50)
const searchMaxPages = ref(5)

// Network
const allowUnknownDomains = ref(false)
const allowHttp = ref(false)

// Subscription Defaults - Search
const defMinDuration = ref('')
const defMaxDuration = ref('')

// Subscription Defaults - Download
const defUseStreamingUrlFiles = ref(false)
const defAudioLanguageMode = ref(0)
const defSelectedAudioLanguages = ref('')
const defDownloadFullVideoSecondaryAudio = ref(false)
const defDetectUndetectedSecondaryAudio = ref(false)
const defDetectCrossResultAudioVariants = ref(false)
const defAddAudioToExistingEpisodes = ref(false)
const defCleanAudioTrackLabels = ref(false)
const defAlwaysCreateSubfolder = ref(false)
const defEnhancedDuplicateDetection = ref(false)
const defAllowFallbackToLowerQuality = ref(true)
const defQualityCheckWithUrl = ref(false)

// Subscription Defaults - Series
const defEnforceSeries = ref(false)
const defExcludeSeries = ref(false)
const defAllowAbsoluteEpisodeNumbering = ref(false)
const defTreatNonEpisodesAsExtras = ref(false)
const defSaveExtrasAsStrm = ref(false)
const defSaveTrailers = ref(true)
const defSaveInterviews = ref(true)
const defSaveGenericExtras = ref(true)

// Subscription Defaults - Metadata
const defOriginalLanguage = ref('')
const defUndefinedOvHandling = ref(1)
const defBackfillAudioLanguages = ref(true)
const defCreateNfo = ref(false)
const defAppendDateToTitle = ref(false)
const defKeepOriginalTitle = ref(false)
const defAppendTimeToTitle = ref(false)

// Subscription Defaults - Accessibility
const defAllowAudioDesc = ref(false)
const defDownloadClearSpeech = ref(false)
const defDetectUndetectedAudioDescription = ref(false)
const defDetectCrossResultAudioDescription = ref(false)
const defAddAudioDescriptionToExistingEpisodes = ref(false)
const defDetectUndetectedClearSpeech = ref(false)
const defDetectCrossResultClearSpeech = ref(false)
const defAddClearSpeechToExistingEpisodes = ref(false)
const defAllowSignLanguage = ref(false)
const defRequiredAudioLanguage = ref('')

// Maintenance
const enableStrmCleanup = ref(false)
const allowDownloadOnUnknownDiskSpace = ref(false)

// The plugin config can deliver enums as numbers or as names, depending on how the host serializes
// them - normalize to the numeric form the radio buttons bind to.
const AUDIO_LANGUAGE_MODE = { All: 0, Selected: 1 }
const UNDEFINED_OV_HANDLING = { UseFallbackLanguage: 0, StoreAsUndetermined: 1, SkipTrack: 2 }

function toEnumValue(value, names, fallback) {
  if (typeof value === 'number') {
    return value
  }
  if (typeof value === 'string' && value in names) {
    return names[value]
  }
  return fallback
}

const duplicateDetectionMessage = 'Ohne "Erweiterte Duplikaterkennung" sieht das Plugin nicht, welche Folgen bereits auf der Platte liegen. '
  + 'Tonspuren, die erst in einem späteren Lauf auftauchen, landen dann als zweites vollständiges Video statt als Tonspur neben der vorhandenen Folge. '
  + 'Bitte die Duplikaterkennung einschalten oder das nachträgliche Hinzufügen abwählen.'

// Attaching a track to an episode that is already on disk needs the library scan; without it the
// plugin cannot know the episode is there and would fetch a second video instead.
const duplicateDetectionConflict = computed(() =>
  (defAddAudioToExistingEpisodes.value
    || defAddAudioDescriptionToExistingEpisodes.value
    || defAddClearSpeechToExistingEpisodes.value)
  && !defEnhancedDuplicateDetection.value)

// The red note sits next to the option that caused it, which is easy to miss on a page this long -
// so the moment the two settings start contradicting each other, say so in front of everything else.
const showDuplicateWarning = ref(false)
watch(duplicateDetectionConflict, (isConflicting, wasConflicting) => {
  if (isConflicting && !wasConflicting) {
    showDuplicateWarning.value = true
  } else if (!isConflicting) {
    showDuplicateWarning.value = false
  }
})

function enableDuplicateDetection() {
  defEnhancedDuplicateDetection.value = true
  showDuplicateWarning.value = false
}

// Computed
const searchTotalItems = computed(() => {
  const ps = parseInt(searchPageSize.value) || 0
  const mp = parseInt(searchMaxPages.value) || 0
  return ps * mp
})

// --- API ---
async function loadConfig() {
  loading.value = true
  try {
    const config = await ApiService.getPluginConfig(PLUGIN_ID)

    lastRun.value = config.LastRun ? new Date(config.LastRun).toLocaleString() : 'Noch nie'

    // Paths
    useTopicForMoviePath.value = config.Paths?.UseTopicForMoviePath ?? false
    defaultDownloadPath.value = config.Paths?.DefaultDownloadPath ?? ''
    subscriptionShowPath.value = config.Paths?.DefaultSubscriptionShowPath ?? ''
    subscriptionMoviePath.value = config.Paths?.DefaultSubscriptionMoviePath ?? ''
    manualShowPath.value = config.Paths?.DefaultManualShowPath ?? ''
    manualMoviePath.value = config.Paths?.DefaultManualMoviePath ?? ''
    tempDownloadPath.value = config.Paths?.TempDownloadPath ?? ''

    // Download
    downloadSubtitles.value = config.Download?.DownloadSubtitles ?? false
    readRate.value = config.Download?.ReadRate ?? 0
    scanLibraryAfterDownload.value = config.Download?.ScanLibraryAfterDownload ?? true
    minFreeDiskSpaceMiB.value = config.Download?.MinFreeDiskSpaceBytes
      ? (config.Download.MinFreeDiskSpaceBytes / (1024 * 1024)).toString()
      : ''

    // Search
    fetchStreamSizes.value = config.Search?.FetchStreamSizes ?? false
    searchInFutureBroadcasts.value = config.Search?.SearchInFutureBroadcasts ?? false
    searchPageSize.value = config.Search?.PageSize ?? 50
    searchMaxPages.value = config.Search?.MaxPages ?? 5

    // Network
    allowUnknownDomains.value = config.Network?.AllowUnknownDomains ?? false
    allowHttp.value = config.Network?.AllowHttp ?? false

    // Maintenance
    enableStrmCleanup.value = config.Maintenance?.EnableStrmCleanup ?? false
    allowDownloadOnUnknownDiskSpace.value = config.Maintenance?.AllowDownloadOnUnknownDiskSpace ?? false

    // Subscription Defaults
    const def = config.SubscriptionDefaults ?? {}
    const defDl = def.DownloadSettings ?? {}
    const defSearch = def.SearchSettings ?? {}
    const defSeries = def.SeriesSettings ?? {}
    const defMeta = def.MetadataSettings ?? {}
    const defAccess = def.AccessibilitySettings ?? {}

    defMinDuration.value = defSearch.MinDurationMinutes ?? ''
    defMaxDuration.value = defSearch.MaxDurationMinutes ?? ''

    defUseStreamingUrlFiles.value = defDl.UseStreamingUrlFiles ?? false
    defAudioLanguageMode.value = toEnumValue(defDl.AudioLanguageMode, AUDIO_LANGUAGE_MODE, 0)
    defSelectedAudioLanguages.value = defDl.SelectedAudioLanguages ?? ''
    defDownloadFullVideoSecondaryAudio.value = defDl.DownloadFullVideoForSecondaryAudio ?? false
    defDetectUndetectedSecondaryAudio.value = defDl.DetectUndetectedSecondaryAudio ?? false
    defDetectCrossResultAudioVariants.value = defDl.DetectCrossResultAudioVariants ?? false
    defAddAudioToExistingEpisodes.value = defDl.AddAudioToExistingEpisodes ?? false
    defCleanAudioTrackLabels.value = defDl.CleanAudioTrackLabels ?? false
    defAlwaysCreateSubfolder.value = defDl.AlwaysCreateSubfolder ?? false
    defEnhancedDuplicateDetection.value = defDl.EnhancedDuplicateDetection ?? false
    defAllowFallbackToLowerQuality.value = defDl.AllowFallbackToLowerQuality !== undefined ? defDl.AllowFallbackToLowerQuality : true
    defQualityCheckWithUrl.value = defDl.QualityCheckWithUrl ?? false

    defEnforceSeries.value = defSeries.EnforceSeriesParsing ?? false
    defExcludeSeries.value = defSeries.ExcludeSeries ?? false
    defAllowAbsoluteEpisodeNumbering.value = defSeries.AllowAbsoluteEpisodeNumbering ?? false
    defTreatNonEpisodesAsExtras.value = defSeries.TreatNonEpisodesAsExtras ?? false
    defSaveExtrasAsStrm.value = defSeries.SaveExtrasAsStrm ?? false
    defSaveTrailers.value = defSeries.SaveTrailers !== undefined ? defSeries.SaveTrailers : true
    defSaveInterviews.value = defSeries.SaveInterviews !== undefined ? defSeries.SaveInterviews : true
    defSaveGenericExtras.value = defSeries.SaveGenericExtras !== undefined ? defSeries.SaveGenericExtras : true

    defOriginalLanguage.value = defMeta.OriginalLanguage ?? ''
    defUndefinedOvHandling.value = toEnumValue(defMeta.UndefinedOriginalVersionHandling, UNDEFINED_OV_HANDLING, 1)
    defBackfillAudioLanguages.value = defMeta.BackfillAudioLanguages ?? true
    defCreateNfo.value = defMeta.CreateNfo ?? false
    defAppendDateToTitle.value = defMeta.AppendDateToTitle ?? false
    defKeepOriginalTitle.value = defMeta.KeepOriginalTitle ?? false
    defAppendTimeToTitle.value = defMeta.AppendTimeToTitle ?? false

    defAllowAudioDesc.value = defAccess.AllowAudioDescription ?? false
    defDownloadClearSpeech.value = defAccess.DownloadClearSpeech ?? false
    defDetectUndetectedAudioDescription.value = defAccess.DetectUndetectedAudioDescription ?? false
    defDetectCrossResultAudioDescription.value = defAccess.DetectCrossResultAudioDescription ?? false
    defAddAudioDescriptionToExistingEpisodes.value = defAccess.AddAudioDescriptionToExistingEpisodes ?? false
    defDetectUndetectedClearSpeech.value = defAccess.DetectUndetectedClearSpeech ?? false
    defDetectCrossResultClearSpeech.value = defAccess.DetectCrossResultClearSpeech ?? false
    defAddClearSpeechToExistingEpisodes.value = defAccess.AddClearSpeechToExistingEpisodes ?? false
    defAllowSignLanguage.value = defAccess.AllowSignLanguage ?? false
    defRequiredAudioLanguage.value = defAccess.RequiredAudioLanguage ?? ''

  } catch (e) {
    console.error('Failed to load config', e)
  } finally {
    loading.value = false
  }
}

async function saveConfig() {
  saving.value = true
  try {
    const config = await ApiService.getPluginConfig(PLUGIN_ID)

    // Paths
    if (!config.Paths) config.Paths = {}
    config.Paths.UseTopicForMoviePath = useTopicForMoviePath.value
    config.Paths.DefaultDownloadPath = defaultDownloadPath.value
    config.Paths.DefaultSubscriptionShowPath = subscriptionShowPath.value
    config.Paths.DefaultSubscriptionMoviePath = subscriptionMoviePath.value
    config.Paths.DefaultManualShowPath = manualShowPath.value
    config.Paths.DefaultManualMoviePath = manualMoviePath.value
    config.Paths.TempDownloadPath = tempDownloadPath.value

    // Download
    if (!config.Download) config.Download = {}
    config.Download.DownloadSubtitles = downloadSubtitles.value
    config.Download.ScanLibraryAfterDownload = scanLibraryAfterDownload.value
    config.Download.ReadRate = readRate.value
    const minFree = parseInt(minFreeDiskSpaceMiB.value)
    config.Download.MinFreeDiskSpaceBytes = isNaN(minFree) ? (1.5 * 1024 * 1024 * 1024) : (minFree * 1024 * 1024)

    // Search
    if (!config.Search) config.Search = {}
    config.Search.FetchStreamSizes = fetchStreamSizes.value
    config.Search.SearchInFutureBroadcasts = searchInFutureBroadcasts.value
    config.Search.PageSize = parseInt(searchPageSize.value) || 50
    config.Search.MaxPages = parseInt(searchMaxPages.value) || 5

    // Network
    if (!config.Network) config.Network = {}
    config.Network.AllowUnknownDomains = allowUnknownDomains.value
    config.Network.AllowHttp = allowHttp.value

    // Maintenance
    if (!config.Maintenance) config.Maintenance = {}
    config.Maintenance.EnableStrmCleanup = enableStrmCleanup.value
    config.Maintenance.AllowDownloadOnUnknownDiskSpace = allowDownloadOnUnknownDiskSpace.value

    // Subscription Defaults
    config.SubscriptionDefaults = {
      SearchSettings: {
        MinDurationMinutes: defMinDuration.value !== '' ? parseInt(defMinDuration.value) : null,
        MaxDurationMinutes: defMaxDuration.value !== '' ? parseInt(defMaxDuration.value) : null
      },
      DownloadSettings: {
        UseStreamingUrlFiles: defUseStreamingUrlFiles.value,
        AudioLanguageMode: defAudioLanguageMode.value,
        SelectedAudioLanguages: defSelectedAudioLanguages.value,
        DownloadFullVideoForSecondaryAudio: defDownloadFullVideoSecondaryAudio.value,
        DetectUndetectedSecondaryAudio: defDetectUndetectedSecondaryAudio.value,
        DetectCrossResultAudioVariants: defDetectCrossResultAudioVariants.value,
        AddAudioToExistingEpisodes: defAddAudioToExistingEpisodes.value,
        CleanAudioTrackLabels: defCleanAudioTrackLabels.value,
        AlwaysCreateSubfolder: defAlwaysCreateSubfolder.value,
        EnhancedDuplicateDetection: defEnhancedDuplicateDetection.value,
        AllowFallbackToLowerQuality: defAllowFallbackToLowerQuality.value,
        QualityCheckWithUrl: defQualityCheckWithUrl.value
      },
      SeriesSettings: {
        EnforceSeriesParsing: defEnforceSeries.value,
        ExcludeSeries: defExcludeSeries.value,
        AllowAbsoluteEpisodeNumbering: defAllowAbsoluteEpisodeNumbering.value,
        TreatNonEpisodesAsExtras: defTreatNonEpisodesAsExtras.value,
        SaveExtrasAsStrm: defSaveExtrasAsStrm.value,
        SaveTrailers: defSaveTrailers.value,
        SaveInterviews: defSaveInterviews.value,
        SaveGenericExtras: defSaveGenericExtras.value
      },
      MetadataSettings: {
        OriginalLanguage: defOriginalLanguage.value,
        UndefinedOriginalVersionHandling: defUndefinedOvHandling.value,
        BackfillAudioLanguages: defBackfillAudioLanguages.value,
        CreateNfo: defCreateNfo.value,
        AppendDateToTitle: defAppendDateToTitle.value,
        KeepOriginalTitle: defKeepOriginalTitle.value,
        AppendTimeToTitle: defAppendTimeToTitle.value
      },
      AccessibilitySettings: {
        AllowAudioDescription: defAllowAudioDesc.value,
        DownloadClearSpeech: defDownloadClearSpeech.value,
        DetectUndetectedAudioDescription: defDetectUndetectedAudioDescription.value,
        DetectCrossResultAudioDescription: defDetectCrossResultAudioDescription.value,
        AddAudioDescriptionToExistingEpisodes: defAddAudioDescriptionToExistingEpisodes.value,
        DetectUndetectedClearSpeech: defDetectUndetectedClearSpeech.value,
        DetectCrossResultClearSpeech: defDetectCrossResultClearSpeech.value,
        AddClearSpeechToExistingEpisodes: defAddClearSpeechToExistingEpisodes.value,
        AllowSignLanguage: defAllowSignLanguage.value,
        RequiredAudioLanguage: defRequiredAudioLanguage.value
      }
    }

     await ApiService.updatePluginConfig(PLUGIN_ID, config)
     if (Dashboard) Dashboard.alert('Einstellungen gespeichert.')
     // Notify parent to refresh config
     emit('config-saved')
  } catch (e) {
    console.error('Failed to save config', e)
    if (Dashboard) Dashboard.alert('Fehler beim Speichern der Einstellungen.')
  } finally {
    saving.value = false
  }
}

async function copyConfig() {
  try {
    const config = await ApiService.getPluginConfig(PLUGIN_ID)
    const text = JSON.stringify(config, null, 2)
    if (window.isSecureContext) {
      await navigator.clipboard.writeText(text)
      if (Dashboard) Dashboard.alert('Konfiguration in die Zwischenablage kopiert.')
    } else {
      prompt('Bitte manuell kopieren:', text)
    }
  } catch (e) {
    console.error('Failed to copy config', e)
  }
}

function selectPath(targetRef, header) {
  if (!Dashboard) return
  try {
    const picker = new Dashboard.DirectoryBrowser()
    picker.show({
      header: header,
      includeDirectories: true,
      includeFiles: false,
      callback: (path) => {
        if (path) targetRef.value = path
        picker.close()
      }
    })
  } catch (e) {
    const newPath = prompt(header + '\nAktueller Pfad: ' + targetRef.value, targetRef.value)
    if (newPath !== null && newPath.trim() !== '') targetRef.value = newPath.trim()
  }
}

async function setupTuner() {
  try {
    if (Dashboard) Dashboard.showLoadingMsg()
    await ApiService.addTunerHost({ Type: 'zapp', Url: 'zapp', FriendlyName: 'Zapp (MediathekView)', TunerCount: 0 })
    if (Dashboard) { Dashboard.hideLoadingMsg(); Dashboard.alert('Zapp Tuner erfolgreich hinzugefügt.') }
  } catch (e) {
    if (Dashboard) Dashboard.hideLoadingMsg()
    console.error('Error adding tuner', e)
    if (Dashboard) Dashboard.alert('Fehler beim Hinzufügen des Tuners.')
  }
}

async function setupGuide() {
  try {
    if (Dashboard) Dashboard.showLoadingMsg()
    await ApiService.addListingProvider({ Type: 'zapp', Id: 'zapp_guide', Name: 'Zapp (MediathekView)' })
    if (Dashboard) { Dashboard.hideLoadingMsg(); Dashboard.alert('Zapp Guide Provider erfolgreich hinzugefügt.') }
  } catch (e) {
    if (Dashboard) Dashboard.hideLoadingMsg()
    console.error('Error adding guide', e)
    if (Dashboard) Dashboard.alert('Fehler beim Hinzufügen des Guide Providers.')
  }
}

onMounted(() => {
  loadConfig()
})
</script>

<template>
  <div class="mvpl-card settings-card">
    <div v-if="loading" class="state-msg"><div class="spinner"></div> Einstellungen werden geladen...</div>

    <form v-else @submit.prevent="saveConfig">

      <!-- ===== ALLGEMEIN (hidden, no active options) ===== -->
      <details hidden class="settings-section">
        <summary class="section-title">Allgemeine Einstellungen</summary>
        <div class="section-body"></div>
      </details>

      <!-- ===== PFADE ===== -->
      <details class="settings-section">
        <summary class="section-title">Pfade-Einstellungen</summary>
        <div class="section-body">
          <div class="checkbox-field">
            <label>
              <input v-model="useTopicForMoviePath" type="checkbox"> Beim Film Downloads Ordner für das Thema erstellen
            </label>
            <p class="field-desc">
              Beim Film Downloads einen Ordner für das Thema (bzw. den Abo-Namen bei Abonnements) erstellen.<br>
              z.B. Wenn an: /Filme/Filme im Ersten/Filmname/Filmname.mkv<br>
              Wenn aus: /Filme/Filmname/Filmname.mkv
            </p>
          </div>

          <div class="field">
            <label class="field-label">Globaler Standard Download Pfad <span class="badge-deprecated">Veraltet</span></label>
            <div class="path-row">
              <input v-model="defaultDownloadPath" type="text" class="field-input" placeholder="Leer lassen für Standard">
              <button type="button" class="btn btn-secondary btn-sm" @click="selectPath(defaultDownloadPath, 'Globalen Standard Download Pfad wählen')" title="Ordner auswählen">📁</button>
            </div>
            <p class="field-desc">Der globale Fallback-Ordner für alle Downloads. <span style="color:#f87171;font-weight:bold;">Hinweis: Dieser Pfad ist veraltet und wird in einer zukünftigen Version entfernt.</span></p>
          </div>

          <div class="field">
            <label class="field-label">Standard Serien Pfad (Abo)</label>
            <div class="path-row">
              <input v-model="subscriptionShowPath" type="text" class="field-input" placeholder="Leer lassen für Standard">
              <button type="button" class="btn btn-secondary btn-sm" @click="selectPath(subscriptionShowPath, 'Standard Serien Pfad (Abo) wählen')" title="Ordner auswählen">📁</button>
            </div>
            <p class="field-desc">Standard-Ordner für Serien-Downloads in Abonnements.</p>
          </div>

          <div class="field">
            <label class="field-label">Standard Film Pfad (Abo)</label>
            <div class="path-row">
              <input v-model="subscriptionMoviePath" type="text" class="field-input" placeholder="Leer lassen für Standard">
              <button type="button" class="btn btn-secondary btn-sm" @click="selectPath(subscriptionMoviePath, 'Standard Film Pfad (Abo) wählen')" title="Ordner auswählen">📁</button>
            </div>
            <p class="field-desc">Standard-Ordner für Film-Downloads in Abonnements.</p>
          </div>

          <div class="field">
            <label class="field-label">Standard Serien Pfad (Manuell)</label>
            <div class="path-row">
              <input v-model="manualShowPath" type="text" class="field-input" placeholder="Leer lassen für Standard">
              <button type="button" class="btn btn-secondary btn-sm" @click="selectPath(manualShowPath, 'Standard Serien Pfad (Manuell) wählen')" title="Ordner auswählen">📁</button>
            </div>
            <p class="field-desc">Standard-Ordner für manuelle Serien-Downloads.</p>
          </div>

          <div class="field">
            <label class="field-label">Standard Film Pfad (Manuell)</label>
            <div class="path-row">
              <input v-model="manualMoviePath" type="text" class="field-input" placeholder="Leer lassen für Standard">
              <button type="button" class="btn btn-secondary btn-sm" @click="selectPath(manualMoviePath, 'Standard Film Pfad (Manuell) wählen')" title="Ordner auswählen">📁</button>
            </div>
            <p class="field-desc">Standard-Ordner für manuelle Film-Downloads.</p>
          </div>

          <div class="field">
            <label class="field-label">Temporärer Download Pfad</label>
            <div class="path-row">
              <input v-model="tempDownloadPath" type="text" class="field-input" placeholder="Leer lassen für direkten Download">
              <button type="button" class="btn btn-secondary btn-sm" @click="selectPath(tempDownloadPath, 'Temporären Download Pfad wählen')" title="Ordner auswählen">📁</button>
            </div>
            <p class="field-desc">Ein optionaler Ordner, in dem Downloads zwischengespeichert werden. Leer lassen, um direkt in den Zielordner herunterzuladen.</p>
          </div>
        </div>
      </details>

      <!-- ===== DOWNLOAD ===== -->
      <details class="settings-section">
        <summary class="section-title">Download-Einstellungen</summary>
        <div class="section-body">
          <div class="checkbox-field">
            <label><input v-model="downloadSubtitles" type="checkbox"> Untertitel herunterladen (wenn verfügbar)</label>
            <p class="field-desc">Lädt eine separate VTT- oder TTML-Datei für Untertitel herunter, falls vom Sender bereitgestellt.</p>
          </div>

          <div class="checkbox-field">
            <label><input v-model="scanLibraryAfterDownload" type="checkbox"> Bibliotheks-Scan nach Download</label>
            <p class="field-desc">Startet automatisch einen Scan der Medienbibliothek, nachdem neue Inhalte heruntergeladen wurden.</p>
          </div>
          <div class="grid-2">
            <div class="field">
              <label class="field-label">Mindestfreier Speicherplatz (MiB)</label>
              <input v-model="minFreeDiskSpaceMiB" type="number" class="field-input" placeholder="z.B. 1536">
              <p class="field-desc">Minimaler freier Speicherplatz um einen neuen Download zu starten.</p>
            </div>
            <div class="field">
              <label class="field-label">Download-Geschwindigkeit</label>
              <input v-model="readRate" type="number" class="field-input" min="0" step="0.1" placeholder="0 = unbegrenzt">
              <p class="field-desc">FFmpeg readrate: 1 = Echtzeit, 2 = doppelte Geschwindigkeit, 0 = unbegrenzt.</p>
            </div>
          </div>
        </div>
      </details>

      <!-- ===== SUCHE ===== -->
      <details class="settings-section">
        <summary class="section-title">Suche-Einstellungen</summary>
        <div class="section-body">
          <div class="checkbox-field">
            <label><input v-model="fetchStreamSizes" type="checkbox"> Größe des Streams abrufen</label>
            <p class="field-desc">Wenn aktiviert, wird die Größe der Video-Dateien bei der Suche abgerufen. Dies verlangsamt die Suche erheblich.</p>
          </div>
          <div class="checkbox-field">
            <label><input v-model="searchInFutureBroadcasts" type="checkbox"> Suche in zukünftigen Ausstrahlungen</label>
            <p class="field-desc">Manchmal sind Videos auch schon vor der eigentlichen TV-Ausstrahlung in der Mediathek verfügbar.</p>
          </div>
          <div class="grid-2">
            <div class="field">
              <label class="field-label">Seitengröße (API-Anfragen)</label>
              <input v-model="searchPageSize" type="number" class="field-input" min="1" max="100">
              <p class="field-desc">Wie viele Ergebnisse pro Seite von der API abgefragt werden.</p>
            </div>
            <div class="field">
              <label class="field-label">Maximale Seitenanzahl</label>
              <input v-model="searchMaxPages" type="number" class="field-input" min="1" max="100">
              <p class="field-desc">Maximale Anzahl an Seiten pro Suchanfrage / Abo-Lauf.</p>
            </div>
          </div>
          <div v-if="searchTotalItems > 0" class="info-msg">
            Aktuelle Konfiguration: Bis zu <strong>{{ searchTotalItems }}</strong> Medien können pro Suche/Abo-Lauf gefunden werden.
          </div>
        </div>
      </details>

      <!-- ===== NETZWERK & SICHERHEIT ===== -->
      <details class="settings-section">
        <summary class="section-title">Netzwerk &amp; Sicherheit</summary>
        <div class="section-body">
          <div class="checkbox-field">
            <label><input v-model="allowUnknownDomains" type="checkbox"> Downloads von unbekannten Domains erlauben</label>
            <p class="field-desc">Ermöglicht das Herunterladen von Inhalten von Domains, die nicht auf der Whitelist stehen. Dies kann nützlich sein, wenn ARD oder ZDF neue CDNs hinzufügen. <strong>Sicherheitsrisiko – mit Vorsicht verwenden.</strong></p>
          </div>
          <div class="checkbox-field">
            <label><input v-model="allowHttp" type="checkbox"> HTTP Downloads erlauben</label>
            <p class="field-desc">Dies kann notwendig sein, da manche URLs kein HTTPS unterstützen. Es wird empfohlen, dies deaktiviert zu lassen.</p>
          </div>
        </div>
      </details>

      <!-- ===== ABO-STANDARDWERTE ===== -->
      <details class="settings-section">
        <summary class="section-title">Abo-Standardwerte</summary>
        <div class="section-body">
          <p class="field-desc" style="margin-bottom:15px;">Diese Werte werden als Standard für neue Abonnements verwendet.</p>

          <div class="sub-section-title">Suche</div>
          <div class="grid-2">
            <div class="field">
              <label class="field-label">Min. Dauer (Minuten)</label>
              <input v-model="defMinDuration" type="number" class="field-input" placeholder="Kein Limit">
            </div>
            <div class="field">
              <label class="field-label">Max. Dauer (Minuten)</label>
              <input v-model="defMaxDuration" type="number" class="field-input" placeholder="Kein Limit">
            </div>
          </div>

          <div class="sub-section-title">Download</div>
          <div class="checkbox-field">
            <label><input v-model="defUseStreamingUrlFiles" type="checkbox"> Streaming-URL-Dateien (.strm) verwenden</label>
            <p class="field-desc">Verwendet Streaming-URL-Dateien (.strm) anstelle des Herunterladens der tatsächlichen Videodateien. Es werden keine Videodateien gespeichert, die Videos werden von ARD/ZDF direkt gestreamt. Untertitel sind hiervon nicht betroffen.</p>
          </div>
          <div class="sub-section-title">Sprachfassungen</div>
          <p v-if="defUseStreamingUrlFiles" class="field-desc">Nicht verfügbar, solange Streaming-URL-Dateien (.strm) aktiv sind - dabei wird keine Datei gespeichert, neben die eine Tonspur gelegt werden könnte.</p>
          <template v-else>
            <div class="radio-field">
              <label><input v-model="defAudioLanguageMode" type="radio" :value="1"> Nur bestimmte Sprachen herunterladen</label>
              <input v-model="defSelectedAudioLanguages" :disabled="defAudioLanguageMode !== 1" type="text" class="field-input" placeholder="ISO-Codes, mit Komma getrennt - z.B. deu, eng">
              <p class="field-desc">Gilt auch für die Hauptspur: Ist deren Sprache nicht dabei, wird stattdessen die passende Fassung als Video geladen. Findet sich gar keine, wird die Sendung übersprungen. Ohne erkennbare Sprache gilt eine Hauptspur als Deutsch.</p>
            </div>
            <div class="radio-field">
              <label><input v-model="defAudioLanguageMode" type="radio" :value="0"> Alle Sprachen herunterladen</label>
              <p class="field-desc">Jede gefundene Sprachfassung wird gespeichert.</p>
            </div>
            <div class="sub-options">
              <div class="checkbox-field">
                <label><input v-model="defDetectUndetectedSecondaryAudio" type="checkbox"> Fehlende Tonspuren finden</label>
                <p class="field-desc">Manche ARD-Titel zeigen in MediathekView nur einen Eintrag, obwohl im Player mehrere Sprachfassungen wählbar sind. Wenn aktiviert, werden solche Fassungen anhand des URL-Musters erkannt und als eigene Tonspur-Datei neben dem Hauptvideo gespeichert.</p>
              </div>
              <div class="checkbox-field">
                <label><input v-model="defDetectCrossResultAudioVariants" type="checkbox"> Verwandte Suchtreffer mit zusätzliche Tonspuren finden</label>
                <p class="field-desc">Manche Sender (arte, ZDF/ZDFneo/3sat) führen dieselbe Sendung als mehrere eigenständige Suchtreffer in unterschiedlichen Sprachen. Wenn aktiviert, werden solche Treffer als zusätzliche Tonspur gespeichert statt als zweites Video.</p>
              </div>
              <div class="checkbox-field">
                <label><input v-model="defAddAudioToExistingEpisodes" type="checkbox"> Sprachfassungen nachträglich zu vorhandenen Folgen hinzufügen</label>
                <p class="field-desc">Taucht eine Sprachfassung erst auf, wenn die Folge schon auf der Platte liegt, wird nur deren Tonspur geladen und daneben gelegt. <strong>Setzt "Erweiterte Duplikaterkennung" voraus.</strong></p>
                <p v-if="duplicateDetectionConflict" class="field-error">{{ duplicateDetectionMessage }}</p>
              </div>
              <div class="checkbox-field">
                <label><input v-model="defDownloadFullVideoSecondaryAudio" type="checkbox"> Vollständiges Video für zusätzliche Sprachfassungen herunterladen</label>
                <p class="field-desc">Wenn aktiviert, wird die zusätzliche Fassung als vollständiges Video geladen. Andernfalls wird nur ihre Tonspur extrahiert.</p>
              </div>
              <div class="field">
                <label class="field-label">Wenn die Sprache der Originalversion nicht bestimmbar ist</label>
                <p class="field-desc">Manche Sender melden nur "Originalversion", ohne die Sprache zu nennen - ARD/ONE liefert dort schlicht "ov".</p>
                <div class="radio-field">
                  <label><input v-model="defUndefinedOvHandling" type="radio" :value="0"> Diesen ISO-Code verwenden</label>
                  <input v-model="defOriginalLanguage" :disabled="defUndefinedOvHandling !== 0" type="text" class="field-input" placeholder="z.B. eng">
                </div>
                <div class="radio-field">
                  <label><input v-model="defUndefinedOvHandling" type="radio" :value="1"> Als "und" (unbestimmt) speichern</label>
                  <p class="field-desc">Die Tonspur wird geladen und behalten, auch wenn oben nur bestimmte Sprachen ausgewählt sind.</p>
                </div>
                <div class="radio-field">
                  <label><input v-model="defUndefinedOvHandling" type="radio" :value="2"> Nicht speichern</label>
                  <p class="field-desc">Ohne Sprache lässt sich nicht prüfen, ob die Spur zur Auswahl oben passt - sie wird dann übersprungen.</p>
                </div>
              </div>
              <div class="checkbox-field">
                <label><input v-model="defBackfillAudioLanguages" type="checkbox"> Sprachcodes nachtragen, sobald die Sprache bekannt wird</label>
                <p class="field-desc">Bereits als "und" gespeicherte Tonspuren werden umbenannt und in der Datei neu getaggt, sobald die Sprache feststeht - egal ob durch einen eingetragenen Code oder weil der Sender sie später doch nennt. Es wird nichts neu kodiert.</p>
              </div>
            </div>
          </template>
          <div class="checkbox-field">
            <label><input v-model="defCleanAudioTrackLabels" type="checkbox"> Tonspur-Bezeichnungen bereinigen</label>
            <p class="field-desc">Entfernt die vom Sender eingebettete Tonspur-Bezeichnung (z.B. "Hessischer Rundfunk mp4toolbox 1.17.1"). Jellyfin erzeugt dann selbst eine saubere, übersetzte Bezeichnung aus Sprache, Codec und Kanälen. Betrifft auch die Hauptspur.</p>
          </div>
          <div class="checkbox-field">
            <label><input v-model="defAlwaysCreateSubfolder" type="checkbox"> Unterordner für Abo erstellen</label>
            <p class="field-desc">Erstellt immer einen Unterordner mit dem Namen des Abonnements, auch wenn es sich um Filme handelt und die globale Einstellung "Beim Film Downloads Ordner für das Thema erstellen" deaktiviert ist.</p>
          </div>
          <div class="checkbox-field">
            <label><input v-model="defEnhancedDuplicateDetection" type="checkbox"> Erweiterte Duplikaterkennung</label>
            <p class="field-desc">Scannt das Zielverzeichnis nach vorhandenen Dateien mit passenden SxxExx-Mustern (oder absoluter Nummerierung), um doppelte Downloads zu vermeiden (auch bei abweichenden Dateinamen).</p>
            <p v-if="duplicateDetectionConflict" class="field-error">{{ duplicateDetectionMessage }}</p>
          </div>
          <div class="checkbox-field">
            <label><input v-model="defAllowFallbackToLowerQuality" type="checkbox"> Fallback auf niedrigere Qualität erlauben</label>
            <p class="field-desc">Wenn aktiviert, wird beim Herunterladen einer Episode geprüft, ob eine niedrigere Qualität verfügbar ist falls die HD-URL nicht gesetzt ist.</p>
          </div>
          <div v-if="defAllowFallbackToLowerQuality" class="sub-options">
            <div class="checkbox-field">
              <label><input v-model="defQualityCheckWithUrl" type="checkbox"> URL-Gültigkeit prüfen</label>
              <p class="field-desc">Wenn aktiviert wird auch geprüft, ob die URLs von MediathekView noch verfügbar sind und ggf. die nächst niedrigere versucht. HD → Default → SD</p>
            </div>
          </div>

          <div class="sub-section-title">Serien</div>
          <div class="checkbox-field">
            <label><input v-model="defEnforceSeries" type="checkbox" :disabled="defExcludeSeries"> Nur Serien herunterladen</label>
            <p class="field-desc">Nur Videos herunterladen, die als Serie erkannt werden</p>
          </div>
          <div class="checkbox-field">
            <label><input v-model="defExcludeSeries" type="checkbox" :disabled="defEnforceSeries"> Keine Serien herunterladen</label>
            <p class="field-desc">Videos, die als Serie erkannt werden, überspringen - z.B. für Abos, die gezielt nur Spielfilme/Einzelsendungen fangen sollen und keine zufällig gleich benannte Serienfolge mit herunterladen wollen.</p>
          </div>
          <div v-if="defEnforceSeries" class="sub-options">
            <div class="checkbox-field">
              <label><input v-model="defAllowAbsoluteEpisodeNumbering" type="checkbox"> Absolute Episodennummerierung erlauben</label>
              <p class="field-desc">Episoden auch herunterladen, wenn nur Absolute Episodennummerierung vorliegt (z.B. "Episode 5" statt "Staffel 1, Episode 5").</p>
            </div>
          </div>
          <div v-if="!defEnforceSeries">
            <div class="checkbox-field">
              <label><input v-model="defTreatNonEpisodesAsExtras" type="checkbox"> Nicht-Episoden als Extras behandeln</label>
              <p class="field-desc">Nicht als Episoden erkannte Videos als Extras behandeln.</p>
            </div>
            <div v-if="defTreatNonEpisodesAsExtras" class="sub-options">
              <div class="checkbox-field">
                <label><input v-model="defSaveExtrasAsStrm" type="checkbox"> Extras als Stream (.strm) speichern</label>
                <p class="field-desc">Extras werden als .strm Dateien gespeichert (spart Speicherplatz).</p>
              </div>
              <div class="checkbox-field">
                <label><input v-model="defSaveTrailers" type="checkbox"> Trailer speichern</label>
                <p class="field-desc">Trailer werden gespeichert.</p>
              </div>
              <div class="checkbox-field">
                <label><input v-model="defSaveInterviews" type="checkbox"> Interviews speichern</label>
                <p class="field-desc">Interviews werden gespeichert.</p>
              </div>
              <div class="checkbox-field">
                <label><input v-model="defSaveGenericExtras" type="checkbox"> Generische Extras speichern</label>
                <p class="field-desc">Alle anderen Extras (nicht Trailer/Interviews) werden gespeichert.</p>
              </div>
            </div>
          </div>

          <div class="sub-section-title">Metadaten</div>
          <div class="checkbox-field">
            <label><input v-model="defCreateNfo" type="checkbox"> NFO Dateien erstellen</label>
            <p class="field-desc">Erstellt eine .nfo Datei mit Metadaten (Beschreibung, Episodennummer) neben der Videodatei.</p>
          </div>
          <div class="checkbox-field">
            <label><input v-model="defAppendDateToTitle" type="checkbox"> Datum an Titel anhängen</label>
            <p class="field-desc">Hängt das Sendedatum an den Titel an (z.B. "Titel - 2026-01-01") und erzwingt die Erkennung als Serie. Nützlich für Sendungen wie "Tagesschau in 100 Sekunden", die kein Release-Datum im Titel haben.</p>
          </div>
          <div v-if="defAppendDateToTitle" class="sub-options">
            <div class="checkbox-field">
              <label><input v-model="defAppendTimeToTitle" type="checkbox"> Uhrzeit an Titel anhängen</label>
              <p class="field-desc">Hängt die Uhrzeit an den Titel an (z.B. "Titel - 2026-01-01 20-00").</p>
            </div>
          </div>
          <div class="checkbox-field">
            <label><input v-model="defKeepOriginalTitle" type="checkbox"> Originaltitel beibehalten</label>
            <p class="field-desc">Behält den Originaltitel bei und entfernt keine Informationen wie (AD), Gebärdensprache oder Episodennummern aus dem Titel.</p>
          </div>

          <div class="sub-section-title">Barrierefreiheit</div>
          <div class="checkbox-field">
            <label><input v-model="defAllowAudioDesc" type="checkbox"> Audiodeskription herunterladen</label>
            <p class="field-desc">Fassungen mit gesprochener Bildbeschreibung. Ist die Option aus, werden solche Fassungen übersprungen.</p>
          </div>
          <div v-if="defAllowAudioDesc" class="sub-options">
            <div class="checkbox-field">
              <label><input v-model="defDetectUndetectedAudioDescription" type="checkbox"> Fehlende Tonspuren finden</label>
              <p class="field-desc">Erkennt Audiodeskription am URL-Muster, auch wenn MediathekView dafür keinen eigenen Eintrag führt.</p>
            </div>
            <div class="checkbox-field">
              <label><input v-model="defDetectCrossResultAudioDescription" type="checkbox"> Verwandte Suchtreffer mit zusätzliche Tonspuren finden</label>
              <p class="field-desc">Führt eigenständige Suchtreffer derselben Folge mit Audiodeskription als zusätzliche Tonspur zusammen.</p>
            </div>
            <div class="checkbox-field">
              <label><input v-model="defAddAudioDescriptionToExistingEpisodes" type="checkbox"> Tonspuren nachträglich zu vorhandenen Folgen hinzufügen</label>
              <p class="field-desc">Taucht die Fassung erst auf, wenn die Folge schon auf der Platte liegt, wird nur ihre Tonspur geladen und daneben gelegt. <strong>Setzt "Erweiterte Duplikaterkennung" voraus.</strong></p>
              <p v-if="duplicateDetectionConflict" class="field-error">{{ duplicateDetectionMessage }}</p>
            </div>
          </div>
          <div class="checkbox-field">
            <label><input v-model="defDownloadClearSpeech" type="checkbox"> Klare Sprache herunterladen</label>
            <p class="field-desc">Fassungen mit sprachoptimiertem Ton ("klare Sprache"). Ist die Option aus, werden solche Fassungen übersprungen.</p>
          </div>
          <div v-if="defDownloadClearSpeech" class="sub-options">
            <div class="checkbox-field">
              <label><input v-model="defDetectUndetectedClearSpeech" type="checkbox"> Fehlende Tonspuren finden</label>
              <p class="field-desc">Erkennt klare Sprache am URL-Muster, auch wenn MediathekView dafür keinen eigenen Eintrag führt.</p>
            </div>
            <div class="checkbox-field">
              <label><input v-model="defDetectCrossResultClearSpeech" type="checkbox"> Verwandte Suchtreffer mit zusätzliche Tonspuren finden</label>
              <p class="field-desc">Führt eigenständige Suchtreffer derselben Folge mit klarer Sprache als zusätzliche Tonspur zusammen.</p>
            </div>
            <div class="checkbox-field">
              <label><input v-model="defAddClearSpeechToExistingEpisodes" type="checkbox"> Tonspuren nachträglich zu vorhandenen Folgen hinzufügen</label>
              <p class="field-desc">Taucht die Fassung erst auf, wenn die Folge schon auf der Platte liegt, wird nur ihre Tonspur geladen und daneben gelegt. <strong>Setzt "Erweiterte Duplikaterkennung" voraus.</strong></p>
              <p v-if="duplicateDetectionConflict" class="field-error">{{ duplicateDetectionMessage }}</p>
            </div>
          </div>
          <div class="checkbox-field">
            <label><input v-model="defAllowSignLanguage" type="checkbox"> Gebärdensprache erlauben</label>
            <p class="field-desc">Lädt auch Inhalte mit Gebärdensprache herunter (sofern verfügbar).</p>
          </div>
          <div class="field">
            <label class="field-label">Nur wenn diese Tonspur verfügbar ist (ISO Code, z.B. 'eng', bei mehreren mit Komma getrennt)</label>
            <input v-model="defRequiredAudioLanguage" type="text" class="field-input" placeholder="leer = kein Filter, z.B. eng oder eng, fra">
            <p class="field-desc">Wenn gesetzt, werden nur Titel heruntergeladen, die eine Tonspur in einer dieser Sprachen haben. Titel ohne passende Tonspur werden komplett übersprungen. Gilt als Standardwert für neue Abos.</p>
          </div>
        </div>
      </details>

      <!-- ===== WARTUNG ===== -->
      <details class="settings-section">
        <summary class="section-title">Wartung</summary>
        <div class="section-body">
          <div class="checkbox-field">
            <label><input v-model="enableStrmCleanup" type="checkbox"> Bereinigung ungültiger Streaming-Dateien (.strm) aktivieren</label>
            <p class="field-desc">Überprüft regelmäßig alle erstellten .strm Dateien auf Gültigkeit der Links. Wenn ein Link nicht mehr erreichbar ist (z.B. 404), wird die Datei gelöscht.</p>
          </div>
          <div class="checkbox-field">
            <label><input v-model="allowDownloadOnUnknownDiskSpace" type="checkbox"> Download bei unbekanntem Speicherplatz erlauben</label>
            <p class="field-desc">Ermöglicht den Download auch dann, wenn der verfügbare Speicherplatz nicht ermittelt werden kann (z.B. bei manchen Netzwerkfreigaben).</p>
          </div>
        </div>
      </details>

      <!-- ===== LIVE TV ===== -->
      <details class="settings-section">
        <summary class="section-title">Live TV Integration</summary>
        <div class="section-body">
          <p class="field-desc">Hier können Sie die Live TV Integration einrichten. Da Jellyfin Plugins nicht automatisch als Guide-Provider anzeigt, können Sie diese hier manuell hinzufügen.</p>
          <div class="btn-row">
            <button type="button" class="btn btn-secondary" @click="setupTuner">Zapp Tuner hinzufügen</button>
            <button type="button" class="btn btn-secondary" @click="setupGuide">Zapp Guide Provider hinzufügen</button>
          </div>
          <p class="field-desc" style="margin-top:10px;"><strong>Hinweis:</strong> Nach dem Hinzufügen müssen Sie möglicherweise die Seite neu laden oder den Guide-Refresh Task in Jellyfin starten, damit die Daten angezeigt werden.</p>
        </div>
      </details>

      <!-- ===== LETZTER LAUF + BUTTONS ===== -->
      <div class="footer-row">
        <div class="last-run">
          <span class="field-label">Letzter Lauf:</span>
          <span>{{ lastRun ?? 'Noch nie' }}</span>
        </div>
        <div class="action-row">
          <span v-if="duplicateDetectionConflict" class="field-error">Bitte den Konflikt bei der Duplikaterkennung beheben.</span>
          <button type="button" class="btn btn-secondary btn-sm" @click="copyConfig" title="Konfiguration kopieren">📋 Kopieren</button>
          <button type="submit" class="btn btn-save" :disabled="saving || duplicateDetectionConflict">
            {{ saving ? 'Speichert...' : 'Speichern' }}
          </button>
        </div>
      </div>

    </form>

    <div v-if="showDuplicateWarning" class="warning-overlay" @click.self="showDuplicateWarning = false">
      <div class="warning-dialog" role="alertdialog" aria-modal="true" aria-labelledby="mvpl-def-dup-warning-title">
        <h3 id="mvpl-def-dup-warning-title">⚠️ Erweiterte Duplikaterkennung fehlt</h3>
        <p>{{ duplicateDetectionMessage }}</p>
        <p class="warning-hint">Bis dahin lassen sich die Einstellungen nicht speichern.</p>
        <div class="warning-actions">
          <button type="button" class="btn btn-secondary" @click="showDuplicateWarning = false">Später</button>
          <button type="button" class="btn btn-save" @click="enableDuplicateDetection">Duplikaterkennung einschalten</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.settings-card {
  padding: 0;
}

.warning-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;
  box-sizing: border-box;
  z-index: 9999;
}

.warning-dialog {
  background: var(--mvpl-surface, #27272a);
  color: var(--mvpl-text-primary, #e4e4e7);
  border: 1px solid var(--mvpl-danger, #f87171);
  border-radius: 8px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.5);
  padding: 20px;
  max-width: 480px;
  width: 100%;
  max-height: 100%;
  overflow-y: auto;
}

.warning-dialog h3 {
  margin: 0 0 12px;
  font-size: 1.05em;
}

.warning-dialog p {
  margin: 0 0 10px;
  font-size: 0.9em;
  line-height: 1.5;
}

.warning-hint {
  color: var(--mvpl-text-secondary, #a1a1aa);
}

.warning-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 16px;
}

.settings-section {
  border-bottom: 1px solid var(--mvpl-border, #3f3f46);
}

.settings-section:last-of-type {
  border-bottom: none;
}

.section-title {
  list-style: none;
  padding: 16px 20px;
  font-weight: 600;
  font-size: 1rem;
  color: var(--mvpl-text-primary, #e4e4e7);
  cursor: pointer;
  user-select: none;
  display: flex;
  align-items: center;
  gap: 8px;
}

.section-title::before {
  content: '▶';
  font-size: 0.7em;
  color: var(--mvpl-accent, #00a4dc);
  transition: transform 0.2s;
}

details[open] > .section-title::before {
  transform: rotate(90deg);
}

.section-title::-webkit-details-marker {
  display: none;
}

.section-body {
  padding: 10px 20px 20px 20px;
  background: var(--mvpl-surface, #1c1c1f);
  border-top: 1px solid var(--mvpl-border, #3f3f46);
}

.grid-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 15px;
}

@media (max-width: 600px) {
  .grid-2 { grid-template-columns: 1fr; }
}

.path-row {
  display: flex;
  gap: 8px;
  align-items: center;
}

.path-row .field-input {
  flex: 1;
}

.sub-options {
  margin-left: 25px;
  border-left: 2px solid var(--mvpl-border, #3f3f46);
  padding-left: 15px;
  margin-top: 5px;
  margin-bottom: 5px;
}

.field-error {
  color: var(--mvpl-danger, #f87171);
  font-size: 0.85em;
  margin-top: 6px;
}

.radio-field {
  margin-bottom: 10px;
}

.radio-field label {
  display: flex;
  align-items: center;
  gap: 8px;
}

.sub-section-title {
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--mvpl-text-secondary, #a1a1aa);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin: 20px 0 10px;
  padding-bottom: 5px;
  border-bottom: 1px solid var(--mvpl-border, #3f3f46);
}

.badge-deprecated {
  background: #7f1d1d;
  color: #fca5a5;
  font-size: 0.7rem;
  padding: 2px 6px;
  border-radius: 4px;
  margin-left: 8px;
  font-weight: 600;
}

.info-msg {
  background: #1e3a5f;
  border: 1px solid #2563eb;
  color: #93c5fd;
  border-radius: 6px;
  padding: 10px 14px;
  font-size: 0.875rem;
  margin-top: 10px;
}

.btn-row {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  margin-top: 10px;
}

.footer-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  background: var(--mvpl-bg, #18181b);
  border-top: 1px solid var(--mvpl-border, #3f3f46);
  flex-wrap: wrap;
  gap: 10px;
}

.last-run {
  display: flex;
  gap: 8px;
  align-items: center;
  font-size: 0.875rem;
  color: var(--mvpl-text-secondary, #a1a1aa);
}

.action-row {
  display: flex;
  gap: 10px;
  align-items: center;
}

.state-msg {
  text-align: center;
  padding: 40px;
  color: var(--mvpl-text-secondary, #a1a1aa);
}
</style>
