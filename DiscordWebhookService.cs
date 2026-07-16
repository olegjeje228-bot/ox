using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using EventHUD.Enums;
using Exiled.API.Features;
using Newtonsoft.Json;

namespace EventHUD
{
    public static class DiscordWebhookService
    {
        // TODO: URL засветился в чате - пересоздай вебхук и вставь новый
        private const string WebhookUrl = "https://discord.com/api/webhooks/1525721529592053840/5CoYrvN1eUhls_z9fY0vCmRBD1tGg1yBMVt9Y1QOgtWmePem_xf7lJEpoomvGbLHV0Pt";

        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        public static string GetRolePing(RPType rpType)
        {
            switch (rpType)
            {
                case RPType.NONRP:
                case RPType.FUNRP:
                    return "<@&1525359474724835440>"; // Анонс ивентов НРП

                case RPType.LIGHTRP:
                case RPType.MEDIUMRP:
                case RPType.HARDRP:
                    return "<@&1525360019674103818>"; // Анонс ивентов РП

                case RPType.FULLRP:
                    return "<@&1525360171541594183>"; // Анонс ивентов ФУЛЛРП

                default:
                    return string.Empty;
            }
        }

        // живые игроки без дамми, чтобы цифра в анонсе была честной
        public static int OnlineCount => Player.List.Count(p => p != null && !p.IsNPC);

        public static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero)
                ts = TimeSpan.Zero;

            int minutes = (int)ts.TotalMinutes;
            return minutes > 0 ? $"{minutes} мин {ts.Seconds} сек" : $"{ts.Seconds} сек";
        }

        /// <summary>
        /// Вызывать из главного потока с уже готовой строкой.
        /// Сама отправка уходит в фон и сервер не блокирует.
        /// </summary>
        public static void Send(string message)
        {
            Log.Info("[Webhook] Send вызван");

            if (string.IsNullOrWhiteSpace(WebhookUrl) || string.IsNullOrWhiteSpace(message))
            {
                Log.Info("[Webhook] пропуск: пустой URL или сообщение");
                return;
            }

            _ = SendAsync(message);
        }

        private static async Task SendAsync(string message)
        {
            try
            {
                // JsonConvert сам экранирует кавычки/переносы из ников и названий ивентов.
                // allowed_mentions обязателен, иначе Discord может молча не пинговать роль.
                string json = JsonConvert.SerializeObject(new
                {
                    content = message,
                    allowed_mentions = new { parse = new[] { "roles" } },
                });

                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    var response = await Client.PostAsync(WebhookUrl, content).ConfigureAwait(false);

                    Log.Info($"[Webhook] Discord ответил {(int)response.StatusCode}");

                    if (!response.IsSuccessStatusCode)
                        Log.Warn($"[Webhook] Discord ответил {(int)response.StatusCode} ({response.ReasonPhrase})");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[Webhook] Не удалось отправить сообщение: {ex.Message}");
            }
        }
    }
}