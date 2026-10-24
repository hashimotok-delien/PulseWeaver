namespace PulseWeaver;

internal sealed class AudioStartupException : Exception
{
    public AudioStartupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class AudioErrorDescriber
{
    public static string Describe(Exception exception)
    {
        var hint = exception.HResult switch
        {
            unchecked((int)0x8889000A) => "デバイスが他のアプリで排他使用中です。そのアプリを終了するか、別のデバイスを選択してください。",
            unchecked((int)0x88890008) => "このデバイスでは指定の音声形式を利用できません。Windows のサウンド設定で既定の形式を確認してください。",
            unchecked((int)0x88890004) => "デバイスが切断されたか無効になっています。デバイスを再接続してください。",
            unchecked((int)0x88890001) => "音声デバイスを初期化できませんでした。",
            unchecked((int)0x80070005) => "音声デバイスへのアクセスが拒否されました。",
            _ => null
        };

        return hint is null
            ? $"{exception.Message} (0x{exception.HResult:X8})"
            : $"{hint} (0x{exception.HResult:X8}: {exception.Message})";
    }
}
