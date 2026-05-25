using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

const string apiUrl =
    "https://api.open-meteo.com/v1/forecast" +
    "?latitude=50.8661" +
    "&longitude=20.6286" +
    "&current=temperature_2m,relative_humidity_2m,precipitation,wind_speed_10m" +
    "&hourly=precipitation_probability" +
    "&timezone=Europe%2FWarsaw" +
    "&forecast_days=1";

Console.WriteLine("Start RPA: Firefox -> pogoda Kielce dla aktualnej godziny");

var options = new FirefoxOptions();

// Ważne: nie włączamy headless, żeby było widać Firefoksa.
// options.AddArgument("--headless");

using IWebDriver driver = new FirefoxDriver(options);

try
{
    driver.Manage().Window.Maximize();

    ShowStep(driver, "Krok 1/3", "Start automatyzacji RPA. Za chwilę otworzę źródło danych pogodowych.", 1800);

    ShowStep(driver, "Krok 2/3", "Otwieram dane pogodowe dla Kielc w przeglądarce Firefox.", 1500);

    driver.Navigate().GoToUrl(apiUrl);

    Thread.Sleep(3000);

    ShowStep(driver, "Krok 3/3", "Odczytuję temperaturę, szansę opadów, wilgotność i wiatr.", 1600);

    using var http = new HttpClient();
    string json = await http.GetStringAsync(apiUrl);

    using var document = JsonDocument.Parse(json);
    JsonElement root = document.RootElement;
    JsonElement current = root.GetProperty("current");

    string currentTime = current.GetProperty("time").GetString() ?? "";
    double temperature = current.GetProperty("temperature_2m").GetDouble();
    int humidity = current.GetProperty("relative_humidity_2m").GetInt32();
    double precipitation = current.GetProperty("precipitation").GetDouble();
    double windSpeed = current.GetProperty("wind_speed_10m").GetDouble();

    int? precipitationProbability = FindCurrentHourValue(
        root,
        currentTime,
        "precipitation_probability"
    );

    string currentHourText = FormatCurrentHour(currentTime);

    string temperatureText = $"{temperature.ToString("0.#", CultureInfo.InvariantCulture)} °C";
    string humidityText = $"{humidity}%";
    string windText = $"{windSpeed.ToString("0.#", CultureInfo.InvariantCulture)} km/h";
    string precipitationText = $"{precipitation.ToString("0.#", CultureInfo.InvariantCulture)} mm";
    string precipitationProbabilityText = precipitationProbability.HasValue
        ? $"{precipitationProbability.Value}%"
        : "brak danych";

    Console.WriteLine();
    Console.WriteLine("Pogoda Kielce — aktualna godzina");
    Console.WriteLine($"Godzina: {currentHourText}");
    Console.WriteLine($"Temperatura: {temperatureText}");
    Console.WriteLine($"Szansa na opady: {precipitationProbabilityText}");
    Console.WriteLine($"Wilgotność: {humidityText}");
    Console.WriteLine($"Wiatr: {windText}");
    Console.WriteLine($"Opad aktualny: {precipitationText}");
    Console.WriteLine();

    string html = BuildWeatherReport(
        currentHourText,
        temperatureText,
        precipitationProbabilityText,
        humidityText,
        windText,
        precipitationText,
        apiUrl
    );

    string reportPath = Path.Combine(Path.GetTempPath(), "pogoda-kielce-rpa.html");
    await File.WriteAllTextAsync(reportPath, html, Encoding.UTF8);

    driver.Navigate().GoToUrl(new Uri(reportPath).AbsoluteUri);

    Console.WriteLine("Raport jest widoczny w Firefoksie.");
    Console.WriteLine("Naciśnij Enter, aby zamknąć przeglądarkę...");
    Console.ReadLine();
}
catch (Exception ex)
{
    Console.WriteLine("Wystąpił błąd:");
    Console.WriteLine(ex.Message);

    Console.WriteLine();
    Console.WriteLine("Naciśnij Enter, aby zakończyć...");
    Console.ReadLine();
}
finally
{
    driver.Quit();
}

static void ShowStep(IWebDriver driver, string title, string message, int waitMs)
{
    string html = $$"""
    <!doctype html>
    <html lang="pl">
    <head>
        <meta charset="utf-8">
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
            body {
                margin: 0;
                min-height: 100vh;
                display: flex;
                align-items: center;
                justify-content: center;
                background: #111827;
                color: #f9fafb;
                font-family: Arial, sans-serif;
            }

            .box {
                width: min(900px, 90vw);
                padding: 48px;
                border-radius: 24px;
                background: #1f2937;
                box-shadow: 0 20px 60px rgba(0,0,0,.35);
            }

            h1 {
                margin-top: 0;
                font-size: 42px;
            }

            p {
                font-size: 24px;
                line-height: 1.5;
            }
        </style>
    </head>
    <body>
        <div class="box">
            <h1>{{WebUtility.HtmlEncode(title)}}</h1>
            <p>{{WebUtility.HtmlEncode(message)}}</p>
        </div>
    </body>
    </html>
    """;

    string dataUrl = "data:text/html;charset=utf-8," + Uri.EscapeDataString(html);
    driver.Navigate().GoToUrl(dataUrl);
    Thread.Sleep(waitMs);
}

static int? FindCurrentHourValue(JsonElement root, string currentTime, string variableName)
{
    if (!DateTime.TryParse(currentTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var currentDateTime))
    {
        return null;
    }

    DateTime currentHour = new(
        currentDateTime.Year,
        currentDateTime.Month,
        currentDateTime.Day,
        currentDateTime.Hour,
        0,
        0
    );

    string currentHourKey = currentHour.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    JsonElement hourly = root.GetProperty("hourly");
    JsonElement times = hourly.GetProperty("time");
    JsonElement values = hourly.GetProperty(variableName);

    for (int i = 0; i < times.GetArrayLength(); i++)
    {
        if (times[i].GetString() == currentHourKey)
        {
            return ReadIntValue(values[i]);
        }
    }

    TimeSpan bestDelta = TimeSpan.MaxValue;
    int? bestValue = null;

    for (int i = 0; i < times.GetArrayLength(); i++)
    {
        string? timeText = times[i].GetString();

        if (DateTime.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var hourlyTime))
        {
            TimeSpan delta = (hourlyTime - currentDateTime).Duration();

            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestValue = ReadIntValue(values[i]);
            }
        }
    }

    return bestValue;
}

static int? ReadIntValue(JsonElement value)
{
    if (value.ValueKind != JsonValueKind.Number)
    {
        return null;
    }

    if (value.TryGetInt32(out int intValue))
    {
        return intValue;
    }

    return (int)Math.Round(value.GetDouble());
}

static string FormatCurrentHour(string currentTime)
{
    if (DateTime.TryParse(currentTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
    {
        return dateTime.ToString("yyyy-MM-dd HH:00", new CultureInfo("pl-PL"));
    }

    return currentTime.Replace("T", " ");
}

static string BuildWeatherReport(
    string currentHour,
    string temperature,
    string precipitationProbability,
    string humidity,
    string wind,
    string precipitation,
    string sourceUrl)
{
    return $$"""
    <!doctype html>
    <html lang="pl">
    <head>
        <meta charset="utf-8">
        <title>Pogoda Kielce — RPA</title>
        <style>
            body {
                margin: 0;
                min-height: 100vh;
                background: #f3f4f6;
                color: #111827;
                font-family: Arial, sans-serif;
            }

            .container {
                max-width: 1100px;
                margin: 0 auto;
                padding: 48px 24px;
            }

            .header {
                margin-bottom: 32px;
            }

            h1 {
                margin: 0 0 12px;
                font-size: 44px;
            }

            .time {
                font-size: 22px;
                color: #4b5563;
            }

            .grid {
                display: grid;
                grid-template-columns: repeat(2, minmax(0, 1fr));
                gap: 24px;
            }

            .card {
                background: white;
                border-radius: 24px;
                padding: 32px;
                box-shadow: 0 10px 30px rgba(0,0,0,.08);
            }

            .label {
                font-size: 18px;
                color: #6b7280;
                margin-bottom: 12px;
            }

            .value {
                font-size: 46px;
                font-weight: 700;
            }

            .footer {
                margin-top: 32px;
                padding: 24px;
                background: #e5e7eb;
                border-radius: 18px;
                color: #374151;
                word-break: break-all;
            }
        </style>
    </head>
    <body>
        <main class="container">
            <section class="header">
                <h1>Pogoda w Kielcach</h1>
                <div class="time">Dane dla aktualnej godziny: {{WebUtility.HtmlEncode(currentHour)}}</div>
            </section>

            <section class="grid">
                <div class="card">
                    <div class="label">Temperatura</div>
                    <div class="value">{{WebUtility.HtmlEncode(temperature)}}</div>
                </div>

                <div class="card">
                    <div class="label">Szansa na opady</div>
                    <div class="value">{{WebUtility.HtmlEncode(precipitationProbability)}}</div>
                </div>

                <div class="card">
                    <div class="label">Wilgotność</div>
                    <div class="value">{{WebUtility.HtmlEncode(humidity)}}</div>
                </div>

                <div class="card">
                    <div class="label">Wiatr</div>
                    <div class="value">{{WebUtility.HtmlEncode(wind)}}</div>
                </div>

                <div class="card">
                    <div class="label">Opad aktualny</div>
                    <div class="value">{{WebUtility.HtmlEncode(precipitation)}}</div>
                </div>
            </section>

            <section class="footer">
                Źródło danych: {{WebUtility.HtmlEncode(sourceUrl)}}
            </section>
        </main>
    </body>
    </html>
    """;
}