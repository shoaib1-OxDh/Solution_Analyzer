using System;
using System.Collections.Generic;

namespace SolutionAnalyzer.Models
{
    public class AnalyzerOptions
    {
        public List<Guid> SolutionIds = new List<Guid>();   // empty = whole environment
        public bool CustomEntitiesOnly = true;
        public bool IncludeSystemFields = false;
        public double FuzzyThreshold = 0.85;      // field-name similarity
        public double OptionSetThreshold = 0.80;  // Jaccard similarity of option labels
        public double LengthUtilThreshold = 50;   // % utilisation below which a field is flagged
        public int SampleLimit = 50000;           // max records analysed per table for length analysis (0 = all)
    }
}
