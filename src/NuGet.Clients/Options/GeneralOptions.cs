// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;

namespace NuGet.Options
{
    public class GeneralOptions
    {
        private const string GeneralSection = "general";
        private const string ExperimentalFeaturesKey = "experimentalFeatures";

        private readonly Configuration.ISettings _settings;

        public GeneralOptions(Configuration.ISettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;
        }

        public bool AreExperimentalFeaturesEnabled
        {
            get
            {
                var settingsValue = _settings.GetValue(GeneralSection, ExperimentalFeaturesKey) ?? string.Empty;
                return IsSet(settingsValue, false);
            }
            set
            {
                _settings.SetValue(GeneralSection, ExperimentalFeaturesKey, value.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static bool IsSet(string value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            value = value.Trim();

            bool boolResult;
            int intResult;

            var result = ((bool.TryParse(value, out boolResult) && boolResult) ||
                          (int.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out intResult) && (intResult == 1)));

            return result;
        }
    }
}
