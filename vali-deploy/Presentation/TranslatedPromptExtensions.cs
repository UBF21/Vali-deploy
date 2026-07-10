using Spectre.Console;

namespace vali_deploy.Presentation;

public static class TranslatedPromptExtensions
{
    public static SelectionPrompt<string> Translated(this SelectionPrompt<string> prompt, string title) =>
        prompt.Title(Translator.T(title)).UseConverter(Translator.T);

    public static MultiSelectionPrompt<string> Translated(this MultiSelectionPrompt<string> prompt, string title) =>
        prompt.Title(Translator.T(title)).UseConverter(Translator.T);

    public static SelectionPrompt<string> TranslatedFormat(this SelectionPrompt<string> prompt, string titleTemplate, params object[] args) =>
        prompt.Title(string.Format(Translator.T(titleTemplate), args)).UseConverter(Translator.T);

    public static MultiSelectionPrompt<string> TranslatedFormat(this MultiSelectionPrompt<string> prompt, string titleTemplate, params object[] args) =>
        prompt.Title(string.Format(Translator.T(titleTemplate), args)).UseConverter(Translator.T);
}
