﻿using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using OmegaPlayer.Features.Configuration.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace OmegaPlayer.Infrastructure.Services
{
    public class LocalizationService : ObservableObject
    {
        private readonly IMessenger _messenger;
        private readonly Dictionary<string, Dictionary<string, string>> _translationCache = new();
        private CultureInfo _currentCulture = new("en");
        private const string DEFAULT_LANGUAGE = "en";
        private const string RESOURCES_PATH = "Resources.Localization";

        // Current translations dictionary that UI can directly bind to
        private Dictionary<string, string> _currentTranslations = new();

        // This is what XAML will bind to directly
        public Dictionary<string, string> Translations => _currentTranslations;

        public string CurrentLanguage => _currentCulture.TwoLetterISOLanguageName;

        public LocalizationService(IMessenger messenger)
        {
            _messenger = messenger;

            // Register for language change messages
            _messenger.Register<LanguageChangedMessage>(this, (r, m) => ChangeLanguage(m.NewLanguage));

            // Load the default language
            LoadLanguage(DEFAULT_LANGUAGE);
            UpdateCurrentTranslations();
        }

        // This method is useful for programmatic access
        public string GetString(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            // First check current translations
            if (_currentTranslations.TryGetValue(key, out var translation))
            {
                return translation;
            }

            // If not found, return the key itself
            return key;
        }

        // for backward compatibility
        public string this[string key] => GetString(key);

        public void ChangeLanguage(string languageCode)
        {
            try
            {
                // Ensure we have a valid language code
                languageCode = string.IsNullOrEmpty(languageCode) ? DEFAULT_LANGUAGE : languageCode.ToLowerInvariant();

                // If language hasn't changed, no need to update
                if (languageCode == _currentCulture.TwoLetterISOLanguageName)
                {
                    return;
                }

                // Set the current culture
                _currentCulture = new CultureInfo(languageCode);

                // Load the language if it's not already loaded
                if (!_translationCache.ContainsKey(languageCode))
                {
                    LoadLanguage(languageCode);
                }

                // Update the current translations dictionary
                UpdateCurrentTranslations();
            }
            catch (Exception ex)
            {
                // If anything fails, fall back to default language
                _currentCulture = new CultureInfo(DEFAULT_LANGUAGE);
            }
        }

        private void UpdateCurrentTranslations()
        {
            // Run on UI thread to ensure UI updates properly
            Action updateAction = () =>
            {
                var languageCode = _currentCulture.TwoLetterISOLanguageName;

                // Create a new dictionary (important for change detection)
                var newTranslations = new Dictionary<string, string>();

                // First add all default language entries as fallback
                if (_translationCache.TryGetValue(DEFAULT_LANGUAGE, out var defaultTranslations))
                {
                    foreach (var kvp in defaultTranslations)
                    {
                        newTranslations[kvp.Key] = kvp.Value;
                    }
                }

                // Then override with current language entries
                if (languageCode != DEFAULT_LANGUAGE &&
                    _translationCache.TryGetValue(languageCode, out var currentTranslations))
                {
                    foreach (var kvp in currentTranslations)
                    {
                        newTranslations[kvp.Key] = kvp.Value;
                    }
                }

                // Replace the entire dictionary
                _currentTranslations = newTranslations;

                // Notify that Translations property has changed
                OnPropertyChanged(nameof(Translations));
            };

            // Ensure we're on the UI thread
            if (Dispatcher.UIThread.CheckAccess())
                updateAction();
            else
                Dispatcher.UIThread.Post(updateAction);
        }

        private void LoadLanguage(string languageCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(languageCode))
                {
                    languageCode = DEFAULT_LANGUAGE;
                }

                var assembly = Assembly.GetExecutingAssembly();
                var resourcePath = $"{assembly.GetName().Name}.{RESOURCES_PATH}.{languageCode}.json";

                using var stream = assembly.GetManifestResourceStream(resourcePath);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var json = reader.ReadToEnd();
                    var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                    if (translations != null)
                    {
                        _translationCache[languageCode] = translations;
                    }
                }
                else
                {
                    _translationCache[languageCode] = new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                _translationCache[languageCode] = new Dictionary<string, string>();
            }
        }
    }
}