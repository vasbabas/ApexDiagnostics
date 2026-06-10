using System;
using ApexDiagnostics.Helpers;

namespace ApexDiagnostics.ViewModels
{
    public class PatchNotesViewModel : ViewModelBase
    {
        public string Version => "v2.0.1";
        public string ReleaseDate => "June 10, 2026";

        public string Description => "This update introduces critical stability fixes for the low-level sector cloner module, resolving the Windows filesystem lock constraints, and optimizes packaged installer distribution channels.";
    }
}
