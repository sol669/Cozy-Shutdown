using System;

namespace ShutdownApp;

public static class Strings
{
    public static bool Ru => App.Settings.Current.Language == AppLanguage.Russian;

    public static string ActionName(PowerActionKind action) => (Ru, action) switch
    {
        (true, PowerActionKind.Shutdown) => "Завершение работы",
        (true, PowerActionKind.Restart) => "Перезапуск",
        (true, PowerActionKind.Sleep) => "Сон",
        (true, PowerActionKind.Hibernate) => "Гибернация",
        (true, PowerActionKind.Lock) => "Блокировка",
        (true, PowerActionKind.Disconnect) => "Отключиться",
        (false, PowerActionKind.Shutdown) => "Shut down",
        (false, PowerActionKind.Restart) => "Restart",
        (false, PowerActionKind.Sleep) => "Sleep",
        (false, PowerActionKind.Hibernate) => "Hibernate",
        (false, PowerActionKind.Lock) => "Lock",
        (false, PowerActionKind.Disconnect) => "Disconnect",
        _ => action.ToString()
    };

    public static string DisconnectRdp => Ru ? "Отключиться" : "Disconnect";
    public static string Settings => Ru ? "Настройки" : "Settings";
    public static string Exit => Ru ? "Выход" : "Exit";
    public static string ScheduledAction => Ru ? "Отложенное действие" : "Scheduled action";
    public static string In30Minutes => Ru ? "Через 30 минут" : "In 30 minutes";
    public static string In1Hour => Ru ? "Через 1 час" : "In 1 hour";
    public static string In3Hours => Ru ? "Через 3 часа" : "In 3 hours";
    public static string CustomInterval => Ru ? "Другой интервал…" : "Custom interval…";
    public static string ChooseDateTime => Ru ? "Указать дату и время…" : "Choose date and time…";
    public static string CancelScheduled => Ru ? "Отменить отложенное действие" : "Cancel scheduled action";
    public static string ScheduledStatus(PowerActionKind action, DateTime when) => Ru
        ? $"Запланировано: {ActionName(action).ToLowerInvariant()} в {when:HH:mm}"
        : $"Scheduled: {ActionName(action)} at {when:t}";
    public static string TrayTip(PowerActionKind defaultAction, PowerActionKind? scheduled, DateTime? when)
        => TrayTip(ActionName(defaultAction), scheduled, when);

    public static string TrayTip(string defaultAction, PowerActionKind? scheduled, DateTime? when)
    {
        string primary = Ru
            ? $"{defaultAction} — двойной щелчок"
            : $"{defaultAction} — double-click";
        if (scheduled is null || when is null) return primary;
        TimeSpan left = when.Value - DateTime.Now;
        string remaining = left.TotalSeconds <= 60
            ? (Ru ? $"через {Math.Max(0, (int)Math.Ceiling(left.TotalSeconds))} сек." : $"in {Math.Max(0, (int)Math.Ceiling(left.TotalSeconds))} sec.")
            : left.TotalHours < 1
                ? (Ru ? $"через {Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} мин." : $"in {Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} min.")
                : (Ru ? $"в {when:HH:mm}" : $"at {when:t}");
        return $"{primary} · {ActionName(scheduled.Value)} {remaining}";
    }

    public static string ConfirmQuestion(PowerActionKind action) => (Ru, action) switch
    {
        (true, PowerActionKind.Shutdown) => "Точно выключить компьютер?",
        (true, PowerActionKind.Restart) => "Точно перезагрузить компьютер?",
        (true, PowerActionKind.Sleep) => "Перевести компьютер в спящий режим?",
        (true, PowerActionKind.Hibernate) => "Перевести компьютер в режим гибернации?",
        (true, PowerActionKind.Lock) => "Заблокировать компьютер?",
        (true, PowerActionKind.Disconnect) => "Отключиться от удаленного сеанса?",
        (false, PowerActionKind.Shutdown) => "Shut down the computer?",
        (false, PowerActionKind.Restart) => "Restart the computer?",
        (false, PowerActionKind.Sleep) => "Put the computer to sleep?",
        (false, PowerActionKind.Hibernate) => "Hibernate the computer?",
        (false, PowerActionKind.Lock) => "Lock the computer?",
        (false, PowerActionKind.Disconnect) => "Disconnect from the remote session?",
        _ => ActionName(action)
    };

    public static string AutoConfirm(int seconds) => Ru
        ? $"Автоматическое подтверждение через {seconds} сек."
        : $"Automatic confirmation in {seconds} sec.";
    public static string Yes => Ru ? "Да" : "Yes";
    public static string No => Ru ? "Нет" : "No";
    public static string Cancel => Ru ? "Отмена" : "Cancel";
    public static string Schedule => Ru ? "Запланировать" : "Schedule";
    public static string ScheduledNotification(PowerActionKind action, DateTime when) => Ru
        ? $"{ActionName(action)} запланировано на {when:dd.MM, HH:mm}."
        : $"{ActionName(action)} is scheduled for {when:g}.";
    public static string ScheduleCancelled => Ru ? "Отложенное действие отменено." : "Scheduled action cancelled.";
    public static string ReplaceScheduleQuestion(DateTime when) => Ru
        ? $"Уже запланировано действие на {when:dd.MM, HH:mm}. Заменить его?"
        : $"An action is already scheduled for {when:g}. Replace it?";
    public static string ExitWithScheduleQuestion => Ru
        ? "Есть активное отложенное действие. Выйти и отменить его?"
        : "A scheduled action is active. Exit and cancel it?";
    public static string ScheduleMissed(PowerActionKind action) => Ru
        ? $"{ActionName(action)} не выполнено: компьютер находился в спящем режиме."
        : $"{ActionName(action)} was not performed because the computer was asleep.";
}
