# 72 Hours Challange Game by Mırmır

72 saatlik bir game jam meydan okuması için sıfırdan yapılmış, üçüncü şahıs aksiyon / hafif korku oyunu. Bir zindanda uyanıyorsun; hırtları temizliyor, çift kılıçlı nöbetçiyi geçiyor, bossu deviriyor ve kaya yağmurunun altında tepeye tırmanarak kaçıyorsun.

**Unity 6000.5.5f1 · URP · New Input System · Platform: Windows**

![Boss odasına giden koridor — heykel, meşaleler, görev paneli ve yetenek göstergeleri](Docs/boss-corridor.png)

<p align="center">
  <img src="Docs/boss.png" width="70%" alt="Boss odasında bossla yüz yüze">
</p>

| | |
|---|---|
| Geliştirme süresi | 16–19 Ağustos 2026 (72 saat) |
| Sürüm | v1.0 Alpha |
| Tür | 3. şahıs aksiyon, combo dövüş, kısa kampanya |
| Süre | ~10-15 dakika |

---

## Oynanış

Oyun tek sahnede (`Assets/Scenes/SampleScene.unity`) geçer ve dört aşamalı doğrusal bir ilerleme kurgusu vardır. Her aşama bitmeden bir sonraki odanın kapısı açılmaz.

| # | Aşama | Görev |
|---|---|---|
| 1 | **Hırt Odası** | Odaya girince ıslıkçı hırt ıslığı çalar, uyuyan tüm hırtlar aynı anda uyanır. Hepsini temizle. |
| 2 | **Okyanus Odası** | Yerinden hiç kıpırdamayan, 360° dönen saldırısıyla seni denize savuran BlackSwordsman'ı geç. Suya düşmek anında ölüm. |
| 3 | **Boss Odası** | Kapıdan girerken jump scare + koridordaki heykeller sana döner. Bossu combo ve uçan tekmeyle yen. |
| 4 | **Kaya Tuzağı** | Gökten yuvarlanan taş yağmurunun altında yokuşu tırmanıp tepeye ulaş → kapanış sekansı. |

Ekranın sağ altındaki görev paneli sıradaki hedefi ve kalan düşman sayısını canlı gösterir.

### Kontroller

| Tuş | Aksiyon |
|---|---|
| `W A S D` | Hareket (kamera yönüne göre) |
| `Mouse` | Kamera |
| `Space` | Zıplama |
| `Sol Tık` | Saldırı — ard arda basınca combo zinciri |
| `Space` + `Sol Tık` | **Uçan tekme** — havadayken saldırı; ileri fırlatır, bossu sersemletir |
| `Sağ Tık` | Takla — süre boyunca hasar almazsın (i-frame) |
| `Left Shift` | Dash |

Dash 1 sn, takla 1.5 sn cooldown'lıdır; ikisinin de göstergesi ekrandadır. Düşen portakallar can yeniler.

---

## Çalıştırma

**Hazır build:** `OnurOyunJam/72HoursChallangeGameByMırmır.exe` (build klasörü repoya dahil değildir).

**Kaynaktan:**

```bash
git clone https://github.com/OnurSessiz/72HoursChallangeGameByMrmr.git
```

1. Unity Hub → Add → klasörü seç. Editör sürümü **6000.5.5f1** olmalı (URP 17.5 ve Input System 1.19 bu sürüme bağlı).
2. `Assets/Scenes/SampleScene.unity` sahnesini aç ve Play.

Build almak için: `File → Build Profiles → Windows`, tek sahne olarak `SampleScene` yeterli.

---

## Teknik yapı

### Oyuncu — state machine

Oyuncu kontrolü `if` yığını yerine state machine ile yazıldı. [`PlayerController`](Assets/Scripts/Player/PlayerController.cs) beyindir: aktif state'i tutar, ortak referansları ve tüm tuning değerlerini state'lere dağıtır, cooldown zaman damgalarını state nesnelerinden bağımsız saklar.

| State | İş |
|---|---|
| [`LocomotionState`](Assets/Scripts/Player/LocomotionState.cs) | Varsayılan: kamera-relative hareket, yumuşak rotasyon, diğer state'lere geçişin tetiklendiği yer |
| [`AttackState`](Assets/Scripts/Player/AttackState.cs) | Çok adımlı combo; adım geçişi Animation Event ile sürülür, event kurulmazsa zaman aşımı devreye girer |
| [`AirAttackState`](Assets/Scripts/Player/AirAttackState.cs) | Uçan tekme: yatay hız frenlenmez, hasar penceresi uçuş boyunca açık |
| [`DashState`](Assets/Scripts/Player/DashState.cs) | Kısa süreli yüksek hızlı itiş |
| [`DodgeState`](Assets/Scripts/Player/DodgeState.cs) | Yön kilitli takla; Enter'da i-frame açılır, Exit'te kapanır |
| [`LaunchedState`](Assets/Scripts/Player/LaunchedState.cs) | Knockback. Ayrı state olması şart: Locomotion her FixedUpdate'te `MovePosition` çağırdığı için savrulma aksi halde bir sonraki fizik karesinde siliniyordu |

Girdi [`PlayerInputReader`](Assets/Scripts/Player/PlayerInputReader.cs) üzerinden event olarak yayılır; state'ler `Enter`'da abone olur, `Exit`'te bırakır. Kamera pivotu ([`PlayerLook`](Assets/Scripts/Player/PlayerLook.cs)) bilinçli olarak karakterin child'ı **değildir** — child olsaydı karakter döndükçe hareket yönü kayardı.

### Dövüş

Vuruş, animasyonun vuruş frame'indeki Animation Event ile tetiklenir; [`PlayerAttack`](Assets/Scripts/Player/PlayerAttack.cs) oyuncunun önünde koni açılı küre taraması yapar. Bulduğu her `IDamageable` bir combo adımında yalnızca **bir kez** hasar alır, yani çok collider'lı düşmanlar çoklu hasar yemez. Combo adımları ayrı ayrı ayarlanır — son vuruş daha sert, daha geniş ve hit-stop'lu.

Hasar tek bir sözleşme üzerinden akar: [`IDamageable`](Assets/Scripts/Combat/IDamageable.cs). Düşman/boss/kırılabilir her şeyin canı [`Health`](Assets/Scripts/Combat/Health.cs), oyuncununki [`PlayerHealth`](Assets/Scripts/Player/PlayerHealth.cs). Boss ayrıca `IStunnable`: uçan tekme yiyince sersemler, o sırada yürümez ve saldırmaz. Okyanus gibi ani ölüm alanları, düşen objeler ve sürekli hasar bölgeleri hepsi tek bir [`DamageSource`](Assets/Scripts/World/DamageSource.cs) ile kurulur.

### İlerleme

Tek otorite [`GameProgress`](Assets/Scripts/Progression/GameProgress.cs); hangi adımın bittiğini yalnızca o bilir. Bir hedef tamamlanınca `StageCompleted` event'i yayılır, o adımı bekleyen [`StageGate`](Assets/Scripts/Progression/StageGate.cs) kapıları collider'larını kapatır ve oda açılır. Hedefler iki çeşit: [`EnemyClearObjective`](Assets/Scripts/Progression/EnemyClearObjective.cs) (listedeki canlar bitince) ve [`ReachPointObjective`](Assets/Scripts/Progression/ReachPointObjective.cs) (trigger'a girince). `enforceOrder` açıkken bir adım, öncekiler bitmeden tamamlanmış sayılmaz — odalar zaten collider'la kilitli olsa da ikinci güvenlik.

Son adım bitince [`EndingSequence`](Assets/Scripts/Progression/EndingSequence.cs) kontrolü keser, sırayla kapanış kameralarını gösterir ve oyunu bitirir.

### Atmosfer

- [`HirtRoomAmbush`](Assets/Scripts/Enemy/HirtRoomAmbush.cs) — sinematik kamera, ıslık, ardından odanın topluca uyanması
- [`BossRoomTurn`](Assets/Scripts/Boss/BossRoomTurn.cs) — jump scare: scare animasyonu, kamera sarsıntısı, oyuncu kilidi
- [`StatueWatcher`](Assets/Scripts/Boss/StatueWatcher.cs) — heykeller oyuncuya döner; smooth takip, ani seğirme ve *sadece bakmıyorken dönen* Weeping Angel modu var
- [`RollingBallSpawner`](Assets/Scripts/World/RollingBallSpawner.cs) + [`RollingBall`](Assets/Scripts/World/RollingBall.cs) — gökten doğup yokuş aşağı yuvarlanan taşlar; süre dolunca, haritadan düşünce ya da bir yere sıkışınca kendini temizler

### Editör araçları

Sahne kurulumunun büyük kısmı elle sürükleme yerine `Assets/Editor` altındaki menü komutlarıyla yapılır — 72 saatte aynı kurulumu tekrar tekrar yapmamak için:

```
Tools/Progression/Setup Game Flow      → GameProgress + üç kapı + üç hedefi bağlar
Tools/Player/Create Health Bar         → oyuncu can barı
Tools/Player/Create Ability Buttons    → dash/dodge cooldown göstergeleri
Tools/UI/Create Quest HUD              → görev paneli
Tools/Combat/Setup Player Attack       → saldırı bileşenleri
Tools/Enemy/Create Enemy Animator      → düşman animator controller'ı
Tools/Boss/Create Boss Animator        → boss animator
Tools/World/Create Rolling Ball Trap   → spawner + tetik alanını kurup birbirine bağlar
Tools/World/Create Orange Pickup       → can eşyası + VFX slotları
Tools/World/Create Ocean Death Zone    → okyanus ani ölüm hacmi
```

Tam liste `Tools/` menüsünde. Her setup komutunun ne yaptığı, bağlı olduğu runtime script'in dosya başındaki açıklamasında da anlatılır.

---

## Proje yapısı

```
Assets/
├─ Scenes/SampleScene.unity     tek oyun sahnesi
├─ Scripts/
│  ├─ Player/                   state machine, girdi, saldırı, can, kamera
│  ├─ Enemy/                    hırt takibi, BlackSwordsman, hırt odası pususu
│  ├─ Boss/                     boss dövüşü, jump scare, heykeller
│  ├─ Combat/                   Health, IDamageable
│  ├─ Progression/              GameProgress, kapılar, hedefler, kapanış
│  ├─ World/                    hasar kaynakları, yuvarlanan taşlar, loot, pickup
│  └─ UI/                       can barları, görev paneli, cooldown göstergeleri
├─ Editor/                      sahne kurulum araçları (Tools/ menüsü)
├─ Animations/                  animator controller'ları
├─ Models/                      karakter, boss, düşman modelleri ve texture'ları
├─ Prefabs/                     OrangePickup, RollingBall
└─ 3rdPartyAssets/              hazır paketler (aşağıya bak)
```

Kod içi dokümantasyon Türkçedir; her script'in başında ne yaptığı, neden öyle yazıldığı ve sahnede nasıl kurulacağı XML doc comment olarak durur.

---

## Kullanılan hazır varlıklar

Karakter, boss ve düşman modelleri ile animasyonlar bu jam için yapıldı. Ortam ve efektler için kullanılan ücretsiz Asset Store paketleri:

- **LowPolyDungeons Lite** — zindan ortamı
- **KE Statues Lite** — heykeller
- **RPG Tiny Fantasy Forest PBR** — dış mekân / ada
- **Playground Apocalypse** — çevre objeleri
- **Eric VFX Studio – Free Game VFX** — efektler
- **fabreffect – Free Slash VFX** — kılıç izleri
- **PolyOne – Free Fruits** — can eşyası (portakal)

---

## Bilinen sınırlar

v1.0 Alpha, 72 saatte biten hâlidir:

- Ana menü, ayarlar menüsü ve kayıt sistemi yok — ölünce sahne baştan yüklenir
- Ses tasarımı eksik: birçok script'te (`StatueWatcher`, `BossRoomTurn`, `StageGate`) klip slotları hazır ama boş
- Tuşlar sabit; yeniden atama yok
- Sadece klavye + mouse; gamepad binding'leri kurulmadı
- Sadece Windows build'i alındı

---

## Geliştirme günlüğü

| Gün | İş |
|---|---|
| 16 Ağu | Proje kurulumu, URP |
| 17 Ağu | Scriptler, ilk model, level planı, animasyonlar, harita + texture, boss odası, jump scare |
| 18 Ağu | Dövüş mekanikleri, level design, boss fight düzeltmeleri, hırt odası, BlackSwordsman |
| 19 Ağu | v1.0 Alpha |
