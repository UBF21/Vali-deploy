using Spectre.Console;

namespace vali_deploy.Presentation;

public static class TranslatedPromptExtensions
{
    public static SelectionPrompt<string> Translated(this SelectionPrompt<string> prompt, string title) =>
        prompt.Title(Translator.T(title)).UseConverter(Translator.T);

    public static MultiSelectionPrompt<string> Translated(this MultiSelectionPrompt<string> prompt, string title) =>
        prompt.Title(Translator.T(title)).UseConverter(Translator.T);

    public static SelectionPrompt<string> TranslatedFormat(this SelectionPrompt<string> prompt, string titleTemplate, params object[] args) =>
        prompt.Title(FormatTranslatedTitle(titleTemplate, args)).UseConverter(Translator.T);

    public static MultiSelectionPrompt<string> TranslatedFormat(this MultiSelectionPrompt<string> prompt, string titleTemplate, params object[] args) =>
        prompt.Title(FormatTranslatedTitle(titleTemplate, args)).UseConverter(Translator.T);

    /// <summary>
    /// Translates <paramref name="titleTemplate"/> via <see cref="Translator.T"/> and then substitutes
    /// <paramref name="args"/> into the resulting (translated) template with <see cref="string.Format(string, object[])"/>.
    /// Extracted so the translate-then-format composition can be unit tested without depending on Spectre.Console's
    /// prompt object shape (internal, exposed to vali-deploy.Tests via InternalsVisibleTo).
    /// </summary>
    internal static string FormatTranslatedTitle(string titleTemplate, params object[] args) =>
        string.Format(Translator.T(titleTemplate), args);
}
