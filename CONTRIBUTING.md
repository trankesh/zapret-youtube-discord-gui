# Разработка

Нужны Windows x64 и .NET Framework 4.8. GUI — C# 5 / WPF, без Electron и npm.

```powershell
.\build.ps1 -OutputDirectory build\staging
powershell -NoProfile -ExecutionPolicy Bypass -File tests\Features.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests\DnsControl.Tests.ps1
.\release.ps1
```

Изменяйте версию в VERSION и описывайте пользовательские изменения в CHANGELOG.md.
Для UI используйте `build/staging/Zapret.exe --preview`: отрисовки сохраняются
в build/artifacts. Проверьте обычный/продвинутый режимы и масштаб 150%.

Тесты сетевых изменений должны использовать временные файлы и подменённые команды.
Не выполняйте в CI запись системного hosts, смену DNS, установку службы или запуск
WinDivert. Реальные административные проверки выполняются отдельно по RELEASE.md.

PR должен описывать проблему, итоговое поведение и выполненные проверки. Сохраните
пользовательские списки, настройки и резервные копии при обновлении приложения.
