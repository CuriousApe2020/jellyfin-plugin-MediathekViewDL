export const SubscriptionFactory = {
  createDefault: (defaults = {}) => ({
    Name: '',
    IsEnabled: true,
    IsVirtual: false,
    Search: {
      Criteria: [{ Fields: ['Title', 'Topic'], Query: '', IsExclude: false }],
      MinDurationMinutes: defaults.SearchSettings?.MinDurationMinutes || null,
      MaxDurationMinutes: defaults.SearchSettings?.MaxDurationMinutes || null,
      MinBroadcastDate: null,
      MaxBroadcastDate: null
    },
    Download: {
      DownloadPath: '',
      UseStreamingUrlFiles: defaults.DownloadSettings?.UseStreamingUrlFiles || false,
      AlwaysCreateSubfolder: defaults.DownloadSettings?.AlwaysCreateSubfolder || false,
      AllowFallbackToLowerQuality: defaults.DownloadSettings?.AllowFallbackToLowerQuality ?? true,
      EnhancedDuplicateDetection: defaults.DownloadSettings?.EnhancedDuplicateDetection || false,
      QualityCheckWithUrl: defaults.DownloadSettings?.QualityCheckWithUrl || false,
      DownloadFullVideoForSecondaryAudio: defaults.DownloadSettings?.DownloadFullVideoForSecondaryAudio || false,
      AudioLanguageMode: defaults.DownloadSettings?.AudioLanguageMode ?? 0,
      SelectedAudioLanguages: defaults.DownloadSettings?.SelectedAudioLanguages || '',
      DetectUndetectedSecondaryAudio: defaults.DownloadSettings?.DetectUndetectedSecondaryAudio || false,
      DetectCrossResultAudioVariants: defaults.DownloadSettings?.DetectCrossResultAudioVariants || false,
      AddAudioToExistingEpisodes: defaults.DownloadSettings?.AddAudioToExistingEpisodes || false,
      CleanAudioTrackLabels: defaults.DownloadSettings?.CleanAudioTrackLabels || false
    },
    Series: {
      EnforceSeriesParsing: defaults.SeriesSettings?.EnforceSeriesParsing || false,
      ExcludeSeries: defaults.SeriesSettings?.ExcludeSeries || false,
      AllowAbsoluteEpisodeNumbering: defaults.SeriesSettings?.AllowAbsoluteEpisodeNumbering || false,
      TreatNonEpisodesAsExtras: defaults.SeriesSettings?.TreatNonEpisodesAsExtras || false,
      SaveTrailers: defaults.SeriesSettings?.SaveTrailers ?? true,
      SaveInterviews: defaults.SeriesSettings?.SaveInterviews ?? true,
      SaveGenericExtras: defaults.SeriesSettings?.SaveGenericExtras ?? true,
      SaveExtrasAsStrm: defaults.SeriesSettings?.SaveExtrasAsStrm || false
    },
    Metadata: {
      OriginalLanguage: defaults.MetadataSettings?.OriginalLanguage || '',
      UndefinedOriginalVersionHandling: defaults.MetadataSettings?.UndefinedOriginalVersionHandling ?? 1,
      BackfillAudioLanguages: defaults.MetadataSettings?.BackfillAudioLanguages ?? true,
      CreateNfo: defaults.MetadataSettings?.CreateNfo || false,
      AppendDateToTitle: defaults.MetadataSettings?.AppendDateToTitle || false,
      KeepOriginalTitle: defaults.MetadataSettings?.KeepOriginalTitle || false,
      AppendTimeToTitle: defaults.MetadataSettings?.AppendTimeToTitle || false
    },
    Accessibility: {
      AllowAudioDescription: defaults.AccessibilitySettings?.AllowAudioDescription || false,
      DownloadClearSpeech: defaults.AccessibilitySettings?.DownloadClearSpeech ?? false,
      DetectUndetectedAccessibilityAudio: defaults.AccessibilitySettings?.DetectUndetectedAccessibilityAudio || false,
      DetectCrossResultAccessibilityVariants: defaults.AccessibilitySettings?.DetectCrossResultAccessibilityVariants || false,
      AddAccessibilityAudioToExistingEpisodes: defaults.AccessibilitySettings?.AddAccessibilityAudioToExistingEpisodes || false,
      AllowSignLanguage: defaults.AccessibilitySettings?.AllowSignLanguage || false,
      RequiredAudioLanguage: defaults.AccessibilitySettings?.RequiredAudioLanguage || ''
    }
  })
};
