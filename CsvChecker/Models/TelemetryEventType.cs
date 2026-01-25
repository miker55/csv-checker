namespace CsvChecker.Models
{
    public static class TelemetryEventType
    {
        public const string Upload = "upload";
        public const string AnalysisCompleted = "analysis_completed";
        public const string AnalysisFailed = "analysis_failed";
        public const string Download = "download";
        public const string FileTooLarge = "file_too_large";
        public const string NoFileSelected = "no_file_selected";
        public const string WrongFileType = "wrong_file_type";
    }
}
