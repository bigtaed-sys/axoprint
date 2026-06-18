using System.Net;
using System.Text;
using AxoPrint.Relay.Stores;

namespace AxoPrint.Relay;

/// <summary>Renders the minimal web upload UI (login + print panel).</summary>
public static class WebUi
{
    private const string Style = """
        <style>
          :root { color-scheme: light dark; }
          body { font-family: system-ui, sans-serif; max-width: 560px; margin: 40px auto; padding: 0 16px; }
          h1 { font-size: 22px; }
          .card { border: 1px solid #8884; border-radius: 10px; padding: 20px; margin-top: 16px; }
          label { display: block; margin: 12px 0 4px; font-weight: 600; }
          input[type=text], input[type=password], input[type=number], select, input[type=file] {
            width: 100%; padding: 8px; border: 1px solid #8886; border-radius: 6px; box-sizing: border-box;
            background: transparent; color: inherit; }
          .row { display: flex; gap: 18px; align-items: center; margin-top: 12px; }
          .row label { display: inline; margin: 0; font-weight: 400; }
          button { margin-top: 18px; padding: 10px 18px; border: 0; border-radius: 6px;
            background: #2563EB; color: #fff; font-size: 15px; cursor: pointer; }
          .msg { margin-top: 14px; padding: 10px; border-radius: 6px; background: #2563EB22; }
          .muted { opacity: .7; font-size: 13px; }
          a { color: #2563EB; }
        </style>
        """;

    public static string Login(string? error)
    {
        string err = error is null ? "" : $"<div class='msg'>{Enc(error)}</div>";
        return $$"""
            <!doctype html><html lang="ru"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>AxoPrint</title>{{Style}}</head><body>
            <h1>AxoPrint</h1>
            <div class="card">
              <form method="post" action="/web/login">
                <label>Пароль</label>
                <input type="password" name="password" autofocus required>
                <button type="submit">Войти</button>
              </form>
              {{err}}
            </div>
            </body></html>
            """;
    }

    public static string Panel(PrinterRegistry reg, string? message)
    {
        var options = new StringBuilder();
        foreach (var p in reg.All)
            options.Append($"<option value=\"{Enc(p.QueueId)}\">{Enc(p.DisplayName)}</option>");
        if (reg.All.Count == 0)
            options.Append("<option value=\"\">(агент ещё не зарегистрировал принтеры)</option>");

        string msg = message is null ? "" : $"<div class='msg'>{Enc(message)}</div>";
        string agent = reg.AgentOnline ? "● агент онлайн" : "○ агент офлайн (задания подождут)";

        return $$"""
            <!doctype html><html lang="ru"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>AxoPrint — печать</title>{{Style}}</head><body>
            <h1>AxoPrint — печать</h1>
            <div class="muted">{{agent}} · <a href="/web/logout">выйти</a></div>
            <div class="card">
              <form method="post" action="/web/print" enctype="multipart/form-data">
                <label>Принтер</label>
                <select name="queue" required>{{options}}</select>

                <label>Файл (PDF, JPG, PNG)</label>
                <input type="file" name="file" accept=".pdf,.jpg,.jpeg,.png" required>

                <label>Копии</label>
                <input type="number" name="copies" value="1" min="1" max="99">

                <div class="row">
                  <span><input type="checkbox" name="duplex" id="d"><label for="d">Двусторонняя</label></span>
                  <span><input type="checkbox" name="mono" id="m"><label for="m">Чёрно-белая</label></span>
                </div>

                <button type="submit">Печать</button>
              </form>
              {{msg}}
            </div>
            <p class="muted">Файл уходит на принтер через ваш сервер. Большие PDF могут грузиться несколько секунд.</p>
            </body></html>
            """;
    }

    /// <summary>MIME type for a supported upload, or null if unsupported.</summary>
    public static string? FormatFor(string fileName)
    {
        string ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => null,
        };
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
