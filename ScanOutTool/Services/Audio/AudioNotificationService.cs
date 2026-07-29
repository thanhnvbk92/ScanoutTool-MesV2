using System;
using System.IO;
using System.Media;

namespace ScanOutTool.Services
{
    public class AudioNotificationService : IAudioNotificationService
    {
        private readonly ILoggingService _loggingService;

        public AudioNotificationService(ILoggingService loggingService)
        {
            _loggingService = loggingService;
        }

        public void PlayBeep()
        {
            Play("ScannerBeepSound.wav");
        }

        public void PlayNg()
        {
            Play("scan_fail.wav");
        }

        private void Play(string fileName)
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
                using var player = new SoundPlayer(filePath);
                player.Play();
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Lỗi phát âm thanh: " + ex.Message);
            }
        }
    }
}
