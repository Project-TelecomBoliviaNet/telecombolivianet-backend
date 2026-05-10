namespace TelecomBoliviaNet.Application.Interfaces;

public interface IChatbotDirectNotifier
{
    /// <summary>
    /// Sends a free-form WhatsApp message via the chatbot endpoint while the
    /// user-initiated 24h conversation window is open (no template cost).
    /// Returns true on success, false on any error — caller must fallback to INotifPublisher.
    /// </summary>
    Task<bool> SendPaymentConfirmationAsync(
        string  phoneNumber,
        string  clientName,
        decimal amount,
        string  coveredPeriod);
}
