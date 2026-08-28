using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Mafi;
using Mafi.Localization;

namespace GeologyReservoirEngineering;

// A minimal, dependency-free JSON translation loader. Captain of Industry mods are not
// required to use any particular localization system; this one loads a flat key/value
// dictionary from Translations/<lang>.json, with a language-fallback chain. Keys use a stable
// "<category>.<protoId>.<field>" convention (e.g. "build-machine.WaterInjectionPump.name")
// rather than the English text itself, so editing the English copy later never breaks a lookup.
internal static class ModTranslation {

	private static Dictionary<string, string> s_translations = new();
	private static bool s_initialized;

	/// <summary>
	/// Loads Translations/&lt;lang&gt;.json from the mod's root directory. Resolution
	/// order: exact current-culture id (e.g. "it-IT") -> two-letter language code (e.g.
	/// "it") -> "en" -> untranslated fallback strings if nothing is found. Call once,
	/// early, from IMod.RegisterPrototypes.
	/// </summary>
	public static void Initialize(string modRootPath) {
		if (s_initialized) {
			return;
		}
		s_initialized = true;

		try {
			string cultureInfoId = LocalizationManager.CurrentLangInfo.CultureInfoId;
			string twoLetter = cultureInfoId.Length >= 2 ? cultureInfoId.Substring(0, 2) : "en";
			string dir = Path.Combine(modRootPath, "Translations");

			string? candidate = null;
			if (cultureInfoId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) {
				candidate = Path.Combine(dir, cultureInfoId + ".json");
			}
			if (candidate == null || !File.Exists(candidate)) {
				candidate = Path.Combine(dir, twoLetter + ".json");
			}
			if (!File.Exists(candidate)) {
				candidate = Path.Combine(dir, "en.json");
			}

			if (File.Exists(candidate)) {
				s_translations = ParseJson(File.ReadAllText(candidate, System.Text.Encoding.UTF8));
				Log.Info($"[Geology & Reservoir Engineering] Loaded translations from '{candidate}' ({s_translations.Count} keys).");
			} else {
				Log.Info("[Geology & Reservoir Engineering] No translation file found, using built-in English fallback strings.");
			}
		} catch (Exception ex) {
			Log.Error($"[Geology & Reservoir Engineering] Failed to load translations: {ex}");
		}
	}

	/// <summary>
	/// Looks up a structured translation key (e.g. "build-machine.WaterInjectionPump.name").
	/// Returns <paramref name="fallbackEnglish"/> unchanged if the key is missing (no
	/// translation file loaded, or the current language has no entry for it yet).
	/// </summary>
	public static string Get(string key, string fallbackEnglish) {
		if (s_translations.Count > 0 && s_translations.TryGetValue(key, out string? value)) {
			return value;
		}
		return fallbackEnglish;
	}

	private static Dictionary<string, string> ParseJson(string json) {
		var dictionary = new Dictionary<string, string>();
		foreach (Match item in Regex.Matches(json, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"")) {
			string key = Unescape(item.Groups[1].Value);
			string value = Unescape(item.Groups[2].Value);
			dictionary[key] = value;
		}
		return dictionary;
	}

	private static string Unescape(string s) {
		return s.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\t", "\t");
	}
}
