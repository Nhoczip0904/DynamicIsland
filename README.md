# Dynamic Island cho Windows

Một "hòn đảo" nhỏ nằm ở mép trên màn hình Windows, lấy cảm hứng từ Dynamic Island của iPhone. Đảo hiện nhạc đang phát, âm lượng và độ sáng. Viết bằng WPF trên .NET 10.

## Tính năng

- **Nhạc đang phát.** Lấy từ điều khiển media của Windows nên chạy với Spotify, trình duyệt, Media Player…
  - Lúc thu gọn: tên bài kèm thanh nhạc nhảy theo màu bìa album.
  - Rê chuột vào thì đảo mở to: bìa, tên bài, nút bài trước / phát-dừng / bài tiếp, thanh thời gian bấm hoặc kéo để tua.
  - Đổi bài thì đảo tự mở ra một lúc, giống iOS.
- **Màu theo bìa album.** Lấy màu nổi bật nhất của bìa, không lấy màu trung bình để khỏi ra màu nâu đục.
- **Âm lượng và độ sáng.** Chỉnh là đảo hiện mức % trong 1,5 giây. Độ sáng chỉ có trên laptop hoặc màn hình liền máy.
- **Nhân vật GIF.** Chọn một ảnh GIF đứng cạnh đảo, nhún theo tiếng nhạc đang phát.
- **Không làm phiền.** Không lấy focus của app đang dùng và không hiện trong Alt+Tab. Hoạt động đúng trên nhiều màn hình có tỉ lệ scale khác nhau.
- **Menu chuột phải:**
  - chạy khi khởi động Windows;
  - chọn màn hình hiển thị;
  - chọn hoặc bỏ nhân vật;
  - thoát.

## Yêu cầu

- Windows 10 (bản 19041) trở lên.
- [.NET 10 SDK](https://dotnet.microsoft.com/download) để build.

## Chạy

```bash
dotnet run --project DynamicIsland
```

Tạo bản cài:

```bash
dotnet publish DynamicIsland -c Release -o publish
```

Chạy test (xUnit):

```bash
dotnet test DynamicIsland.Tests
```

Cài đặt (màn hình, nhân vật) được lưu ở `%APPDATA%\DynamicIsland\settings.json`. Tuỳ chọn chạy khi khởi động ghi vào khoá `HKCU\...\Run`.

## Cấu trúc

| File | Vai trò |
|---|---|
| `MainWindow.xaml(.cs)` | Giao diện đảo, các trạng thái thu gọn / mở to / mức âm lượng, media, menu |
| `Palette.cs` | Chọn màu nhấn từ bìa album |
| `Timeline.cs` | Tính vị trí bài hát giữa hai lần player báo |
| `SystemLevels.cs` | Theo dõi âm lượng (NAudio) và độ sáng (WMI) |

## Thư viện

[NAudio](https://github.com/naudio/NAudio), System.Management, [XamlAnimatedGif](https://github.com/XamlAnimatedGif/XamlAnimatedGif).
