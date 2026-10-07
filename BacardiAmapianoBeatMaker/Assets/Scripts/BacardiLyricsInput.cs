using UnityEngine;

// IMGUI's character/font filtering must not alter clipboard lyrics.
public static class BacardiLyricsInput
{
    public const string ControlName = "Bacardi.DefaultHookLyrics";

    static int StringIndex(string text, int characterIndex)
    {
        int offset = 0;
        for (int character = 0; character < characterIndex && offset < text.Length; character++) {
            if (offset + 1 < text.Length && ((text[offset] == '\r' && text[offset + 1] == '\n')
                || (char.IsHighSurrogate(text[offset]) && char.IsLowSurrogate(text[offset + 1])))) offset++;
            offset++;
        }
        return offset;
    }

    static void ReplaceSelection(TextEditor editor, string replacement)
    {
        int start = StringIndex(editor.text, System.Math.Min(editor.cursorIndex, editor.selectIndex));
        int end = StringIndex(editor.text, System.Math.Max(editor.cursorIndex, editor.selectIndex));
        string updated = editor.text.Substring(0, start) + replacement + editor.text.Substring(end);
        // Let Unity update its cursor and editing history, then restore the exact string.
        editor.ReplaceSelection(replacement);
        editor.text = updated;
    }

    public static bool HandlePaste(Event input, TextEditor editor)
    {
        bool clipboardCommand = input.type == EventType.ValidateCommand || input.type == EventType.ExecuteCommand;
        bool clipboardShortcut = input.type == EventType.KeyDown && (input.control || input.command) && !input.alt;
        bool copy = (clipboardCommand && input.commandName == "Copy") || (clipboardShortcut && input.keyCode == KeyCode.C);
        bool cut = (clipboardCommand && input.commandName == "Cut") || (clipboardShortcut && input.keyCode == KeyCode.X);
        bool select = (clipboardCommand && input.commandName == "SelectAll") || (clipboardShortcut && input.keyCode == KeyCode.A);
        if (copy || cut || select) {
            if (input.type != EventType.ValidateCommand) {
                if (select) editor.SelectAll();
                else {
                    if (editor.hasSelection) {
                        // Unity 6 cursor indices count rendered characters, not UTF-16 code units.
                        int start = StringIndex(editor.text, System.Math.Min(editor.cursorIndex, editor.selectIndex));
                        int end = StringIndex(editor.text, System.Math.Max(editor.cursorIndex, editor.selectIndex));
                        GUIUtility.systemCopyBuffer = editor.text.Substring(start, end - start);
                        if (cut) { ReplaceSelection(editor, ""); GUI.changed = true; }
                    }
                }
            }
            input.Use();
            return true;
        }
        bool command = (input.type == EventType.ValidateCommand || input.type == EventType.ExecuteCommand)
            && input.commandName == "Paste";
        bool shortcut = input.type == EventType.KeyDown && input.keyCode == KeyCode.V
            && (input.control || input.command) && !input.alt;
        if (!command && !shortcut) return false;
        if (input.type != EventType.ValidateCommand)
        {
            // Replace the selection without character/font filtering or newline normalization.
            ReplaceSelection(editor, GUIUtility.systemCopyBuffer ?? "");
            GUI.changed = true;
        }
        input.Use();
        return true;
    }

    public static string Draw(string lyrics)
    {
        return Draw(lyrics, ControlName, 90);
    }

    public static string Draw(string lyrics, string controlName, float height)
    {
        if (GUI.enabled && GUI.GetNameOfFocusedControl() == controlName)
        {
            var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            if (HandlePaste(Event.current, editor)) lyrics = editor.text;
        }
        GUI.SetNextControlName(controlName);
        // No UI truncation: the provider reports its request limit instead of altering pasted text.
        return GUILayout.TextArea(lyrics ?? "", GUILayout.Height(height));
    }
}
