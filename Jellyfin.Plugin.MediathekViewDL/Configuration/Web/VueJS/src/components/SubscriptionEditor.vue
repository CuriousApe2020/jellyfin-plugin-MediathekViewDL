<script setup>
import {computed, ref, watch} from 'vue'
import { MS_PER_DAY_MINUS_ONE } from '../utils/Constants'
import ApiService from '../utils/ApiService'

const props = defineProps({
    subscription: {
        type: Object,
        default: null
    }
})

const emit = defineEmits(['save', 'cancel'])

const editedSub = ref(null)
const activeTab = ref('basic')

// The plugin config can deliver enums as numbers or as names, depending on how the host
// serializes them - normalize to the numeric form the radio buttons bind to.
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

// Attaching a track to an episode that is already on disk needs the library scan; without it the
// plugin cannot know the episode is there and would fetch a second video instead.
const duplicateDetectionMessage = 'Ohne "Erweiterte Duplikaterkennung" sieht das Plugin nicht, welche Folgen bereits auf der Platte liegen. '
    + 'Tonspuren, die erst in einem späteren Lauf auftauchen, landen dann als zweites vollständiges Video statt als Tonspur '
    + 'neben der vorhandenen Folge. Bitte die Duplikaterkennung einschalten oder das nachträgliche Hinzufügen abwählen.'
const duplicateDetectionConflict = computed(() => {
    const download = editedSub.value?.Download ?? {}
    const accessibility = editedSub.value?.Accessibility ?? {}
    const wantsAttaching = download.AddAudioToExistingEpisodes
        || accessibility.AddAudioDescriptionToExistingEpisodes
        || accessibility.AddClearSpeechToExistingEpisodes
    return Boolean(wantsAttaching) && !download.EnhancedDuplicateDetection
})

// The red note sits next to the option that caused it, which is easy to miss when the conflict is
// created from the other tab - so the moment the two settings start contradicting each other, say so
// in front of everything else.
const showDuplicateWarning = ref(false)
watch(duplicateDetectionConflict, (isConflicting, wasConflicting) => {
    if (isConflicting && !wasConflicting) {
        showDuplicateWarning.value = true
    } else if (!isConflicting) {
        showDuplicateWarning.value = false
    }
})

function enableDuplicateDetection() {
    if (editedSub.value?.Download) {
        editedSub.value.Download.EnhancedDuplicateDetection = true
    }

    showDuplicateWarning.value = false
}
const availableChannels = ref([])
const availableTopics = ref([])

const Dashboard = window.Dashboard ?? null

async function loadAutocompleteData() {
    try {
        const [channels, topics] = await Promise.all([
            ApiService.getChannels(),
            ApiService.getTopics()
        ])
        availableChannels.value = channels || []
        availableTopics.value = topics || []
    } catch (e) {
        console.error('Failed to load autocomplete data', e)
    }
}

loadAutocompleteData()

watch(() => props.subscription, (newVal) => {
    if (newVal) {
        // Deep copy
        const copy = JSON.parse(JSON.stringify(newVal))
        // Ensure nested objects are initialized to prevent template crashes
        copy.Search = copy.Search || {}
        copy.Search.Criteria = copy.Search.Criteria || []
        copy.Download = copy.Download || {}
        copy.Series = copy.Series || {}
        copy.Metadata = copy.Metadata || {}
        copy.Accessibility = copy.Accessibility || {}
        copy.Download.AudioLanguageMode = toEnumValue(copy.Download.AudioLanguageMode, AUDIO_LANGUAGE_MODE, 0)
        copy.Metadata.UndefinedOriginalVersionHandling = toEnumValue(copy.Metadata.UndefinedOriginalVersionHandling, UNDEFINED_OV_HANDLING, 1)
        copy.Metadata.BackfillAudioLanguages = copy.Metadata.BackfillAudioLanguages ?? true
        copy.Accessibility.DownloadClearSpeech = copy.Accessibility.DownloadClearSpeech ?? false
        editedSub.value = copy
        // Reset active tab when a new subscription is opened
        activeTab.value = 'basic'
    } else {
        editedSub.value = null
    }
}, {immediate: true, deep: true})

function addQuery() {
    editedSub.value.Search.Criteria.push({
        Fields: ['Title', 'Topic'],
        Query: '',
        IsExclude: false
    })
}

function removeQuery(index) {
    editedSub.value.Search.Criteria.splice(index, 1)
}

function toggleField(query, field) {
    const index = query.Fields.indexOf(field)
    if (index > -1) {
        if (query.Fields.length > 1) {
            query.Fields.splice(index, 1)
        }
    } else {
        query.Fields.push(field)
    }
}

async function save() {
    emit('save', editedSub.value)
}

function cancel() {
    emit('cancel')
}

function selectPath() {
    if (!Dashboard) return
    const picker = new Dashboard.DirectoryBrowser()
    picker.show({
        header: 'Abo Pfad wählen',
        includeDirectories: true,
        includeFiles: false,
        callback: (path) => {
            if (path) {
                editedSub.value.Download.DownloadPath = path
            }
            picker.close()
        }
    })
}

// Utility to format date for input[type=date]
function formatDate(dateStr) {
    if (!dateStr) return ''
    return dateStr.split('T')[0]
}

function updateDate(target, field, value) {
    if (!value) {
        target[field] = null
        return
    }
    let date = new Date(value)
    if (field === 'MaxBroadcastDate') {
        date = new Date(date.getTime() + MS_PER_DAY_MINUS_ONE)
    }
    target[field] = date.toISOString()
}
</script>

<template>
    <!-- Teleported to body by PluginConfig, so it carries mvpl-scope itself. -->
    <div v-if="editedSub" class="editor-overlay mvpl-scope">
        <div class="editor-modal mvpl-card">
            <header class="editor-header">
                <h2>{{ editedSub.Id ? 'Abonnement bearbeiten' : 'Neues Abonnement' }}</h2>
                <div class="header-actions">
                    <button @click="cancel" class="btn-icon">✕</button>
                </div>
            </header>

            <div class="editor-tabs">
                <button class="tab-btn" :class="{ active: activeTab === 'basic' }" @click="activeTab = 'basic'">Allgemein</button>
                <button class="tab-btn" :class="{ active: activeTab === 'search' }" @click="activeTab = 'search'">Suche</button>
                <button class="tab-btn" :class="{ active: activeTab === 'download' }" @click="activeTab = 'download'">Download</button>
                <button class="tab-btn" :class="{ active: activeTab === 'series' }" @click="activeTab = 'series'">Serien</button>
                <button class="tab-btn" :class="{ active: activeTab === 'metadata' }" @click="activeTab = 'metadata'">Metadaten</button>
                <button class="tab-btn" :class="{ active: activeTab === 'accessibility' }" @click="activeTab = 'accessibility'">Barrierefreiheit</button>
            </div>

            <div class="editor-content">
                <!-- Allgemein Tab -->
                <div v-if="activeTab === 'basic'" class="tab-pane">
                    <div class="field">
                        <label>Name (Serienname)</label>
                        <input v-model="editedSub.Name" type="text" class="field-input" placeholder="z.B. Tatort" required>
                    </div>
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.IsEnabled" type="checkbox"> Aktiviert
                        </label>
                    </div>

                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.IsVirtual" type="checkbox"> Virtuell (nur Kanal, kein Download)
                        </label>
                        <p class="field-desc">Sendungen werden nicht heruntergeladen und es werden keine STRMs erstellt. Stattdessen erscheinen sie im Jellyfin-Kanal und werden direkt aus der Mediathek gestreamt.</p>
                    </div>

                    <div class="checkbox-field" hidden>
                        <label>
                            <input v-model="editedSub.IgnoreLocalFiles" type="checkbox"> Lokale Dateien ignorieren
                        </label>
                        <p class="field-desc">Erzwingt den Download, auch wenn die Datei bereits lokal existiert.</p>
                    </div>
                    <div class="checkbox-field" hidden>
                        <label>
                            <input v-model="editedSub.IgnoreHistory" type="checkbox"> Download-Verlauf ignorieren
                        </label>
                        <p class="field-desc">Erzwingt den Download, auch wenn die Sendung bereits früher geladen wurde.</p>
                    </div>
                </div>

                <!-- Suche Tab -->
                <div v-if="activeTab === 'search'" class="tab-pane">
                    <h3>Suchanfragen</h3>
                    <div v-for="(query, idx) in editedSub.Search.Criteria" :key="idx" class="query-row">
                        <div class="query-fields">
                            <button
                                v-for="f in ['Title', 'Topic', 'Description', 'Channel']"
                                :key="f"
                                @click="toggleField(query, f)"
                                class="field-tag"
                                :class="{ active: query.Fields.includes(f) }"
                            >
                                {{ f === 'Title' ? 'Titel' : f === 'Topic' ? 'Thema' : f === 'Description' ? 'Beschreibung' : 'Sender' }}
                            </button>
                        </div>
                        <div class="query-input-row">
                            <input
                                v-model="query.Query"
                                type="text"
                                class="field-input"
                                :placeholder="query.IsExclude ? 'Ausschließen...' : 'Suchen...'"
                                :list="query.Fields.includes('Channel') && !query.Fields.includes('Topic') ? 'sub-channels' : (query.Fields.includes('Topic') ? 'sub-topics' : null)"
                            >
                            <button @click="query.IsExclude = !query.IsExclude" class="btn-small" :class="{ 'btn-danger': query.IsExclude }">
                                {{ query.IsExclude ? 'NICHT' : 'SUCHE' }}
                            </button>
                            <button @click="removeQuery(idx)" class="btn-icon">🗑️</button>
                        </div>
                    </div>
                    <datalist id="sub-channels">
                        <option v-for="channel in availableChannels" :key="channel" :value="channel" />
                    </datalist>
                    <datalist id="sub-topics">
                        <option v-for="topic in availableTopics" :key="topic" :value="topic" />
                    </datalist>
                    <button @click="addQuery" class="btn btn-secondary">Anfrage hinzufügen</button>

                    <hr>
                    <div class="grid-2">
                        <div class="field">
                            <label>Min. Dauer (Minuten)</label>
                            <input v-model="editedSub.Search.MinDurationMinutes" type="number" class="field-input">
                        </div>
                        <div class="field">
                            <label>Max. Dauer (Minuten)</label>
                            <input v-model="editedSub.Search.MaxDurationMinutes" type="number" class="field-input">
                        </div>
                    </div>

                    <hr>
                    <div class="field">
                        <label>Nur wenn diese Tonspur verfügbar ist (ISO Code, z.B. 'eng', bei mehreren mit Komma getrennt)</label>
                        <input v-model="editedSub.Accessibility.RequiredAudioLanguage" type="text" class="field-input" placeholder="leer = kein Filter, z.B. eng oder eng, fra">
                        <p class="field-desc">Wenn gesetzt, werden nur Titel heruntergeladen, die eine Tonspur in einer dieser Sprachen haben — egal ob MediathekView sie als eigenen Suchtreffer findet oder ob sie erst über die Sprachfassungs-Erkennung im Reiter "Download" gefunden wird. Titel ohne passende Tonspur werden komplett übersprungen, also auch ohne Hauptspur.</p>
                    </div>

                    <hr>
                    <div class="grid-2">
                        <div class="field">
                            <label>Min. Sendedatum</label>
                            <input :value="formatDate(editedSub.Search.MinBroadcastDate)" @input="updateDate(editedSub.Search, 'MinBroadcastDate', $event.target.value)" type="date" class="field-input">
                        </div>
                        <div class="field">
                            <label>Max. Sendedatum</label>
                            <input :value="formatDate(editedSub.Search.MaxBroadcastDate)" @input="updateDate(editedSub.Search, 'MaxBroadcastDate', $event.target.value)" type="date" class="field-input">
                        </div>
                    </div>
                </div>

                <!-- Download Tab -->
                <div v-if="activeTab === 'download'" class="tab-pane">
                    <div class="field">
                        <label>Download Pfad (Optional)</label>
                        <div class="input-with-btn">
                            <input v-model="editedSub.Download.DownloadPath" type="text" class="field-input" placeholder="Wenn leer werden die Standardpfade verwendet">
                            <button @click="selectPath" class="btn btn-secondary">Wählen</button>
                        </div>
                        <p class="field-desc">Leer lassen, um den Standardpfad zu nutzen. Bei Serien wird automatisch ein Unterordner mit dem Abo-Namen erstellt. Bei Filmen wird ein Unterordner mit dem Abo-Namen nur erstellt, wenn die Option "Ordner für das Thema erstellen" in den Einstellungen aktiviert ist.</p>
                    </div>
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Download.UseStreamingUrlFiles" type="checkbox"> Streaming-URL-Dateien (.strm) verwenden
                        </label>
                        <p class="field-desc">Verwendet Streaming-URL-Dateien (.strm) anstelle des Herunterladens der tatsächlichen Videodateien. Es werden keine Videodateien gespeichert, die Videos werden von ARD/ZDF direkt gestreamt. Untertitel sind hiervon nicht betroffen.</p>
                    </div>
                    <h4 class="section-title">Sprachfassungen</h4>
                    <p v-if="editedSub.Download.UseStreamingUrlFiles" class="field-desc">Nicht verfügbar, solange Streaming-URL-Dateien (.strm) aktiv sind - dabei wird keine Datei gespeichert, neben die eine Tonspur gelegt werden könnte.</p>
                    <template v-else>
                        <div class="radio-field">
                            <label>
                                <input v-model="editedSub.Download.AudioLanguageMode" type="radio" :value="1"> Nur bestimmte Sprachen herunterladen
                            </label>
                            <input
                                v-model="editedSub.Download.SelectedAudioLanguages"
                                :disabled="editedSub.Download.AudioLanguageMode !== 1"
                                type="text"
                                class="field-input"
                                placeholder="ISO-Codes, mit Komma getrennt - z.B. deu, eng">
                            <p class="field-desc">Gilt auch für die Hauptspur: Ist deren Sprache nicht dabei, wird stattdessen die passende Fassung als Video geladen. Findet sich gar keine, wird die Sendung übersprungen. Ohne erkennbare Sprache gilt eine Hauptspur als Deutsch.</p>
                        </div>
                        <div class="radio-field">
                            <label>
                                <input v-model="editedSub.Download.AudioLanguageMode" type="radio" :value="0"> Alle Sprachen herunterladen
                            </label>
                            <p class="field-desc">Jede gefundene Sprachfassung wird gespeichert - die Hauptspur und alles, was die Erkennung unten zusätzlich findet.</p>
                        </div>
                        <div class="sub-options">
                            <div class="checkbox-field">
                                <label>
                                    <input v-model="editedSub.Download.DetectUndetectedSecondaryAudio" type="checkbox"> Fehlende Tonspuren finden
                                </label>
                                <p class="field-desc">Manche ARD-Titel zeigen in MediathekView nur einen Eintrag, obwohl im Player mehrere Sprachfassungen wählbar sind. Wenn aktiviert, werden solche Fassungen anhand des URL-Musters erkannt und als eigene Tonspur-Datei neben dem Hauptvideo gespeichert.</p>
                            </div>
                            <div class="checkbox-field">
                                <label>
                                    <input v-model="editedSub.Download.DetectCrossResultAudioVariants" type="checkbox"> Verwandte Suchtreffer mit zusätzliche Tonspuren finden
                                </label>
                                <p class="field-desc">Manche Sender (arte, ZDF/ZDFneo/3sat) führen dieselbe Sendung als mehrere eigenständige Suchtreffer in unterschiedlichen Sprachen. Wenn aktiviert, werden solche Treffer erkannt und als zusätzliche Tonspur gespeichert, statt als zweites, fast identisches Video.</p>
                            </div>
                            <div class="checkbox-field">
                                <label>
                                    <input v-model="editedSub.Download.AddAudioToExistingEpisodes" type="checkbox"> Sprachfassungen nachträglich zu vorhandenen Folgen hinzufügen
                                </label>
                                <p class="field-desc">Taucht eine Sprachfassung erst auf, wenn die Folge schon auf der Platte liegt, wird nur deren Tonspur geladen und daneben gelegt. Ohne diese Option entsteht ein zweites Video. <strong>Setzt "Erweiterte Duplikaterkennung" voraus</strong> - nur sie liest die Bibliothek überhaupt ein.</p>
                                <p v-if="duplicateDetectionConflict" class="field-error">{{ duplicateDetectionMessage }}</p>
                            </div>
                            <div class="checkbox-field">
                                <label>
                                    <input v-model="editedSub.Download.DownloadFullVideoForSecondaryAudio" type="checkbox"> Vollständiges Video für zusätzliche Sprachfassungen herunterladen
                                </label>
                                <p class="field-desc">Wenn aktiviert, wird die zusätzliche Fassung als vollständiges Video geladen. Andernfalls wird nur ihre Tonspur extrahiert und neben das vorhandene Video gelegt.</p>
                            </div>
                            <div class="field">
                                <label>Wenn die Sprache der Originalversion nicht bestimmbar ist</label>
                                <p class="field-desc">Manche Sender melden nur "Originalversion", ohne die Sprache zu nennen - ARD/ONE liefert dort schlicht "ov".</p>
                                <div class="radio-field">
                                    <label>
                                        <input v-model="editedSub.Metadata.UndefinedOriginalVersionHandling" type="radio" :value="0"> Diesen ISO-Code verwenden
                                    </label>
                                    <input
                                        v-model="editedSub.Metadata.OriginalLanguage"
                                        :disabled="editedSub.Metadata.UndefinedOriginalVersionHandling !== 0"
                                        type="text"
                                        class="field-input"
                                        placeholder="z.B. eng">
                                </div>
                                <div class="radio-field">
                                    <label>
                                        <input v-model="editedSub.Metadata.UndefinedOriginalVersionHandling" type="radio" :value="1"> Als "und" (unbestimmt) speichern
                                    </label>
                                    <p class="field-desc">Die Tonspur wird geladen und behalten, auch wenn oben nur bestimmte Sprachen ausgewählt sind.</p>
                                </div>
                                <div class="radio-field">
                                    <label>
                                        <input v-model="editedSub.Metadata.UndefinedOriginalVersionHandling" type="radio" :value="2"> Nicht speichern
                                    </label>
                                    <p class="field-desc">Ohne Sprache lässt sich nicht prüfen, ob die Spur zur Auswahl oben passt - sie wird dann übersprungen.</p>
                                </div>
                            </div>
                            <div class="checkbox-field">
                                <label>
                                    <input v-model="editedSub.Metadata.BackfillAudioLanguages" type="checkbox"> Sprachcodes nachtragen, sobald die Sprache bekannt wird
                                </label>
                                <p class="field-desc">Bereits als "und" gespeicherte Tonspuren werden umbenannt und in der Datei neu getaggt, sobald die Sprache feststeht - egal ob durch einen eingetragenen Code oder weil der Sender sie später doch nennt. Es wird nichts neu kodiert.</p>
                            </div>
                        </div>
                    </template>
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Download.CleanAudioTrackLabels" type="checkbox"> Tonspur-Bezeichnungen bereinigen
                        </label>
                        <p class="field-desc">Entfernt die vom Sender eingebettete Tonspur-Bezeichnung (z.B. "Hessischer Rundfunk mp4toolbox 1.17.1"). Jellyfin erzeugt dann selbst eine saubere, in der Sprache des Benutzers übersetzte Bezeichnung aus Sprache, Codec und Kanälen. Betrifft auch die Hauptspur.</p>
                    </div>
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Download.AlwaysCreateSubfolder" type="checkbox"> Unterordner für dieses Abo erstellen
                        </label>
                        <p class="field-desc">Erstellt immer einen Unterordner mit dem Namen des Abonnements, auch wenn es sich um Filme handelt und die globale Einstellung "Beim Film Downloads Ordner für das Thema erstellen" deaktiviert ist.</p>
                    </div>
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Download.EnhancedDuplicateDetection" type="checkbox"> Erweiterte Duplikaterkennung
                        </label>
                        <p class="field-desc">Scannt das Zielverzeichnis nach vorhandenen Dateien mit passenden SxxExx-Mustern (oder absoluter Nummerierung), um doppelte Downloads zu vermeiden (auch bei abweichenden Dateinamen).</p>
                        <p v-if="duplicateDetectionConflict" class="field-error">{{ duplicateDetectionMessage }}</p>
                    </div>
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Download.AllowFallbackToLowerQuality" type="checkbox"> Fallback auf niedrigere Qualität erlauben
                        </label>
                        <p class="field-desc">Wenn aktiviert, wird beim Herunterladen einer Episode geprüft, ob eine niedrigere Qualität verfügbar ist falls die HD-URL nicht gesetzt ist.</p>
                    </div>
                    <div v-if="editedSub.Download.AllowFallbackToLowerQuality" class="sub-options">
                        <div class="checkbox-field">
                            <label>
                                <input v-model="editedSub.Download.QualityCheckWithUrl" type="checkbox"> Prüft ob die URLs gültig ist.
                            </label>
                            <p class="field-desc">Wenn aktiviert wird auch geprüft, ob die URLs von MediathekView noch verfügbar sind und ggf. die nächst niedrigere versucht. HD → Default → SD</p>
                        </div>
                    </div>
                </div>

                <!-- Serien Tab -->
                <div v-if="activeTab === 'series'" class="tab-pane">
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Series.EnforceSeriesParsing" type="checkbox" :disabled="editedSub.Series.ExcludeSeries"> Nur Serien herunterladen
                        </label>
                        <p class="field-desc">Nur Videos herunterladen, die als Serie erkannt werden</p>
                    </div>
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Series.ExcludeSeries" type="checkbox" :disabled="editedSub.Series.EnforceSeriesParsing"> Keine Serien herunterladen
                        </label>
                        <p class="field-desc">Videos, die als Serie erkannt werden, überspringen - z.B. für Abos, die gezielt nur Spielfilme/Einzelsendungen fangen sollen und keine zufällig gleich benannte Serienfolge mit herunterladen wollen.</p>
                    </div>
                    <div v-if="editedSub.Series.EnforceSeriesParsing" class="sub-options">
                        <div class="checkbox-field">
                            <label>
                                <input v-model="editedSub.Series.AllowAbsoluteEpisodeNumbering" type="checkbox"> Absolute Episodennummerierung erlauben
                            </label>
                            <p class="field-desc">Episoden auch herunterladen, wenn nur Absolute Episodennummerierung vorliegt (z.B. "Episode 5" statt "Staffel 1, Episode 5").</p>
                        </div>
                    </div>
                    <div v-else class="sub-options">
                        <div class="checkbox-field">
                            <label>
                                <input v-model="editedSub.Series.TreatNonEpisodesAsExtras" type="checkbox"> Nicht Episoden als Extras behandeln
                            </label>
                            <p class="field-desc">Nicht als Episoden erkannte Videos als Extras behandeln.</p>
                        </div>
                        <div v-if="editedSub.Series.TreatNonEpisodesAsExtras" class="sub-options">
                            <div class="checkbox-field">
                                <label><input v-model="editedSub.Series.SaveTrailers" type="checkbox"> Trailer speichern</label>
                                <p class="field-desc">Trailer werden gespeichert.</p>
                            </div>
                            <div class="checkbox-field">
                                <label><input v-model="editedSub.Series.SaveInterviews" type="checkbox"> Interviews speichern</label>
                                <p class="field-desc">Interviews werden gespeichert.</p>
                            </div>
                            <div class="checkbox-field">
                                <label><input v-model="editedSub.Series.SaveGenericExtras" type="checkbox"> Generische Extras speichern</label>
                                <p class="field-desc">Alle anderen Extras (nicht Trailer/Interviews) werden gespeichert.</p>
                            </div>
                            <div class="checkbox-field">
                                <label><input v-model="editedSub.Series.SaveExtrasAsStrm" type="checkbox"> Extras als Stream (.strm) speichern</label>
                                <p class="field-desc">Extras werden als .strm Dateien gespeichert (spart Speicherplatz).</p>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- Metadaten Tab -->
                <div v-if="activeTab === 'metadata'" class="tab-pane">
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Metadata.CreateNfo" type="checkbox"> NFO Dateien erstellen
                        </label>
                        <p class="field-desc">Erstellt eine .nfo Datei mit Metadaten (Beschreibung, Episodennummer) neben der Videodatei.</p>
                    </div>
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Metadata.AppendDateToTitle" type="checkbox"> Datum an Titel anhängen
                        </label>
                        <p class="field-desc">Hängt das Sendedatum an den Titel an (z.B. "Titel - 2026-01-01") und erzwingt die Erkennung als Serie. Nützlich für Sendungen wie "Tagesschau in 100 Sekunden", die kein Release-Datum im Titel haben.</p>
                    </div>
                    <div v-if="editedSub.Metadata.AppendDateToTitle" class="sub-options">
                        <div class="checkbox-field">
                            <label>
                                <input v-model="editedSub.Metadata.AppendTimeToTitle" type="checkbox"> Uhrzeit an Titel anhängen
                            </label>
                            <p class="field-desc">Hängt die Uhrzeit an den Titel an (z.B. "Titel - 2026-01-01 20-00").</p>
                        </div>
                    </div>
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Metadata.KeepOriginalTitle" type="checkbox"> Originaltitel beibehalten
                        </label>
                        <p class="field-desc">Behält den Originaltitel bei und entfernt keine Informationen wie (AD), Gebärdensprache oder Episodennummern aus dem Titel.</p>
                    </div>
                </div>

                <!-- Barrierefreiheit Tab -->
                <div v-if="activeTab === 'accessibility'" class="tab-pane">
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Accessibility.AllowAudioDescription" type="checkbox"> Audiodeskription herunterladen
                        </label>
                        <p class="field-desc">Fassungen mit gesprochener Bildbeschreibung. Ist die Option aus, werden solche Fassungen übersprungen.</p>
                    </div>
                    <div v-if="editedSub.Accessibility.AllowAudioDescription" class="sub-options">
                        <div class="checkbox-field">
                            <label>
                                <input v-model="editedSub.Accessibility.DetectUndetectedAudioDescription" type="checkbox"> Fehlende Tonspuren finden
                            </label>
                            <p class="field-desc">Erkennt Audiodeskription am URL-Muster, auch wenn MediathekView dafür keinen eigenen Eintrag führt, und legt sie als Tonspur neben das Hauptvideo.</p>
                        </div>
                        <div class="checkbox-field">
                            <label>
                                <input v-model="editedSub.Accessibility.DetectCrossResultAudioDescription" type="checkbox"> Verwandte Suchtreffer mit zusätzliche Tonspuren finden
                            </label>
                            <p class="field-desc">Führt eigenständige Suchtreffer derselben Folge mit Audiodeskription als zusätzliche Tonspur zusammen, statt sie als zweites Video zu laden.</p>
                        </div>
                        <div class="checkbox-field">
                            <label>
                                <input v-model="editedSub.Accessibility.AddAudioDescriptionToExistingEpisodes" type="checkbox"> Tonspuren nachträglich zu vorhandenen Folgen hinzufügen
                            </label>
                            <p class="field-desc">Taucht die Fassung erst auf, wenn die Folge schon auf der Platte liegt, wird nur ihre Tonspur geladen und daneben gelegt. <strong>Setzt "Erweiterte Duplikaterkennung" im Reiter "Download" voraus.</strong></p>
                            <p v-if="duplicateDetectionConflict" class="field-error">{{ duplicateDetectionMessage }}</p>
                        </div>
                    </div>
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Accessibility.DownloadClearSpeech" type="checkbox"> Klare Sprache herunterladen
                        </label>
                        <p class="field-desc">Fassungen mit sprachoptimiertem Ton ("klare Sprache"). Ist die Option aus, werden solche Fassungen übersprungen.</p>
                    </div>
                    <div v-if="editedSub.Accessibility.DownloadClearSpeech" class="sub-options">
                        <div class="checkbox-field">
                            <label>
                                <input v-model="editedSub.Accessibility.DetectUndetectedClearSpeech" type="checkbox"> Fehlende Tonspuren finden
                            </label>
                            <p class="field-desc">Erkennt klare Sprache am URL-Muster, auch wenn MediathekView dafür keinen eigenen Eintrag führt, und legt sie als Tonspur neben das Hauptvideo.</p>
                        </div>
                        <div class="checkbox-field">
                            <label>
                                <input v-model="editedSub.Accessibility.DetectCrossResultClearSpeech" type="checkbox"> Verwandte Suchtreffer mit zusätzliche Tonspuren finden
                            </label>
                            <p class="field-desc">Führt eigenständige Suchtreffer derselben Folge mit klarer Sprache als zusätzliche Tonspur zusammen, statt sie als zweites Video zu laden.</p>
                        </div>
                        <div class="checkbox-field">
                            <label>
                                <input v-model="editedSub.Accessibility.AddClearSpeechToExistingEpisodes" type="checkbox"> Tonspuren nachträglich zu vorhandenen Folgen hinzufügen
                            </label>
                            <p class="field-desc">Taucht die Fassung erst auf, wenn die Folge schon auf der Platte liegt, wird nur ihre Tonspur geladen und daneben gelegt. <strong>Setzt "Erweiterte Duplikaterkennung" im Reiter "Download" voraus.</strong></p>
                            <p v-if="duplicateDetectionConflict" class="field-error">{{ duplicateDetectionMessage }}</p>
                        </div>
                    </div>
                    <div class="checkbox-field">
                        <label>
                            <input v-model="editedSub.Accessibility.AllowSignLanguage" type="checkbox"> Versionen mit Gebärdensprache herunterladen
                        </label>
                        <p class="field-desc">Lädt auch Inhalte mit Gebärdensprache herunter (sofern verfügbar).</p>
                    </div>
                    <p class="field-desc">Der Filter "Nur wenn diese Tonspur verfügbar ist" steht im Reiter "Suche" unter den Suchanfragen.</p>
                </div>
            </div>

            <footer class="editor-footer">
                <button @click="cancel" class="btn btn-secondary">Abbrechen</button>
                <button @click="$emit('test', editedSub)" class="btn btn-secondary">Abo prüfen (Dry Run)</button>
                <span v-if="duplicateDetectionConflict" class="field-error footer-error">Bitte den Konflikt bei der Duplikaterkennung beheben.</span>
                <button @click="save" class="btn btn-primary" :disabled="duplicateDetectionConflict">Abo Speichern</button>
            </footer>

            <div v-if="showDuplicateWarning" class="warning-overlay" @click.self="showDuplicateWarning = false">
                <div class="warning-dialog" role="alertdialog" aria-modal="true" aria-labelledby="mvpl-dup-warning-title">
                    <h3 id="mvpl-dup-warning-title">⚠️ Erweiterte Duplikaterkennung fehlt</h3>
                    <p>{{ duplicateDetectionMessage }}</p>
                    <p class="warning-hint">Bis dahin lässt sich das Abo nicht speichern.</p>
                    <div class="warning-actions">
                        <button @click="showDuplicateWarning = false" class="btn btn-secondary">Später</button>
                        <button @click="enableDuplicateDetection" class="btn btn-primary">Duplikaterkennung einschalten</button>
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>

<style scoped>
.editor-overlay {
    /* Text tone alongside the background, because both now come from the same measured theme.
       A teleported dialog sits outside .plugin-config, so without this it took its color from
       Jellyfin's body while its background came from our variables - two independent sources that
       can disagree, which is how "light text on a light panel" happens. */
    color: var(--mvpl-text-primary, #e4e4e7);
    /* border-box because this is sized 100% *and* padded: with the default content-box the
       overlay ends up 2x the padding wider and taller than the viewport, which on a phone
       pushes the dialog off the right edge and gives the whole page a horizontal scrollbar. */
    box-sizing: border-box;
    position: fixed;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    background: rgba(0, 0, 0, 0.8);
    display: flex;
    justify-content: center;
    align-items: center;
    z-index: 9999;
    padding: 20px;
}

.editor-modal {
    /* Declared directly rather than relying solely on the shared .card class (combined via
       class="editor-modal mvpl-card") - every other modal in this plugin (see
       AdvancedDownloadDialog.vue's .modal-dialog) sets its own background explicitly, and this
       one didn't, letting the page underneath show through the dialog itself instead of just the
       dimmed overlay around it. Same color as the rest of this page's cards (.mvpl-card in style.css). */
    background: var(--mvpl-bg, #18181b);
    /* Positioned so the warning overlay below can cover exactly this dialog and nothing else. */
    position: relative;
    width: 100%;
    max-width: 800px;
    height: 80vh;
    /* Capped against the viewport, not just a fixed floor: a 500px minimum is taller than the
       usable height of a phone in landscape once the overlay's padding is taken off, and the
       dialog then grew past the screen with its footer buttons out of reach. */
    min-height: min(500px, 100%);
    max-height: 100%;
    display: flex;
    flex-direction: column;
    overflow: hidden;
}

.editor-header {
    padding: 20px;
    border-bottom: 1px solid var(--mvpl-border, #3f3f46);
    display: flex;
    justify-content: space-between;
    align-items: center;
}

.editor-tabs {
    display: flex;
    background: var(--mvpl-surface, #27272a);
    border-bottom: 1px solid var(--mvpl-border, #3f3f46);
    overflow-x: auto;
}

.editor-content {
    padding: 20px;
    overflow-y: auto;
    flex: 1;
}

.warning-overlay {
    /* Inside .editor-modal rather than the page, so the dialog it warns about stays visible behind
       it and the two cannot end up on opposite sides of the screen. */
    position: absolute;
    inset: 0;
    background: rgba(0, 0, 0, 0.6);
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 20px;
    z-index: 10;
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

.editor-footer {
    padding: 20px;
    border-top: 1px solid var(--mvpl-border, #3f3f46);
    display: flex;
    justify-content: flex-end;
    gap: 15px;
}

.tab-btn {
    padding: 12px 20px;
    background: none;
    border: none;
    color: var(--mvpl-text-secondary, #a1a1aa);
    cursor: pointer;
    white-space: nowrap;
}

.tab-btn.active {
    color: var(--mvpl-accent, #00a4dc);
    background: var(--mvpl-bg, #18181b);
    border-bottom: 2px solid var(--mvpl-accent, #00a4dc);
}

.grid-2 {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 20px;
}

.query-row {
    background: var(--mvpl-surface, #27272a);
    padding: 15px;
    border-radius: 8px;
    margin-bottom: 15px;
    border: 1px solid var(--mvpl-border, #3f3f46);
}

.query-fields {
    display: flex;
    gap: 8px;
    margin-bottom: 10px;
}

.field-tag {
    padding: 4px 10px;
    border-radius: 12px;
    background: var(--mvpl-border, #3f3f46);
    border: none;
    color: var(--mvpl-text-secondary, #a1a1aa);
    font-size: 0.75rem;
    cursor: pointer;
}

.field-tag.active {
    background: var(--mvpl-accent, #00a4dc);
    color: var(--mvpl-on-accent, white);
}

.query-input-row {
    display: flex;
    gap: 10px;
    align-items: center;
}

.input-with-btn {
    display: flex;
    gap: 10px;
}

.field-error {
    color: var(--mvpl-danger, #f87171);
    font-size: 0.85em;
    margin-top: 6px;
}

.footer-error {
    margin-right: auto;
    align-self: center;
}

.radio-field {
    margin-bottom: 10px;
}

.radio-field label {
    display: flex;
    align-items: center;
    gap: 8px;
}

.section-title {
    margin: 20px 0 4px;
    font-size: 1.05em;
    font-weight: 600;
    color: var(--mvpl-text-primary, #e4e4e7);
}

.sub-options {
    margin-left: 25px;
    border-left: 2px solid var(--mvpl-border, #3f3f46);
    padding-left: 15px;
    margin-top: 10px;
    margin-bottom: 10px;
}

.btn-small {
    padding: 5px 10px;
    border-radius: 4px;
    border: 1px solid var(--mvpl-border, #3f3f46);
    background: var(--mvpl-surface, #27272a);
    /* Theme text color, not hardcoded white - on a light theme, --mvpl-surface is a light
       background, and white text on it is unreadable. */
    color: var(--mvpl-text-primary, #e4e4e7);
    cursor: pointer;
    font-size: 0.75rem;
}

.btn-danger {
    background: #ef4444;
    border-color: #ef4444;
}
</style>
