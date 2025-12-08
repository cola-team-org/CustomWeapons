## Custom Weapons
Данный плагин добавляет на сервер возможность установить кастомные скины на оружие

## Для работы плагина требуется: 
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)
- [Metamod:Source](https://www.metamodsource.net/downloads.php/?branch=master)

## Команды:
- **css_cw** - Меню скинов
- **css_cw_give** - Выдать скин игроку (Пример css_cw_give <ник/юзер_айди/#стим_айди> <название_скина>)
- **css_cw_remove** - Забрать скин у игрока (Пример css_cw_give <ник/юзер_айди/#стим_айди> <название_скина>)

## Конфиг:
```
{
  "WeaponList": { // Список скинов
    "weapon_m4a1": { // Название оружия на которое нужно одеть скин
      "Тестовая модель": { // Название скина
        "Model": "phase2/weapons/models/2en0w/m4a4_zaomeng/m4a4_zaomeng_ag2.vmdl", // Путь к модели
        "CustomName": "M4A1 | Аниме девочка" // Кастомное название оружия (можно оставить пустым)
      }
    }
  },
  "FreeSkinsAll": true, // Бесплатные ли все скины или их нужно выдавать
  "DatabaseHost": "000.000.000.000", // Хост базы данных
  "DatabasePort": 3306, // Порт базы данных
  "DatabaseUser": "user", // Пользователь базы данных
  "DatabasePassword": "pass", // Пароль от пользователя базы данных
  "DatabaseName": "database" // Название базы данных
}
```
## Установка:
Раскидать все по папкам и настроить конфиг
