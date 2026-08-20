# FortiScope

FortiScope, bir FortiGate cihazından SNMPv3 ile CPU, RAM, aktif session ve IF-MIB interface değerlerini okuyup Razor Pages dashboard'unda gösteren .NET 8 uygulamasıdır. Dashboard üzerinde simülasyon verisi kullanılmaz.

## Gereksinimler

- .NET 8 SDK
- FortiGate üzerinde SNMPv3 `authPriv` kullanıcısı
- Uygulamanın UDP/161 üzerinden `192.168.64.2` adresine erişebilmesi

Gizli olmayan bağlantı ayarları `appsettings.json` içindeki `Snmp` bölümündedir. SNMP authentication ve privacy parolaları kaynak dosyalarda tutulmaz.

## User Secrets yapılandırması

Proje klasöründe gerçek parolalarınızı aşağıdaki komutlarla kaydedin:

```bash
dotnet user-secrets set "Snmp:AuthPassword" "GERCEK_AUTH_PAROLASI"
dotnet user-secrets set "Snmp:PrivacyPassword" "GERCEK_PRIVACY_PAROLASI"
```

`GERCEK_...` bölümlerini FortiGate üzerinde tanımladığınız değerlerle değiştirin. Bu değerler proje klasörüne veya Git'e yazılmaz. Ayarları doğrulamak için anahtar adlarını, değerleri ekrana basmadan, `dotnet user-secrets list` ile kontrol edebilirsiniz.

## Çalıştırma

```bash
dotnet restore
dotnet run
```

Tarayıcıda terminalde gösterilen yerel adresi açın. Arka plan servisi cihazı iki saniyede bir sorgular. Parolalar eksikse veya cihaz erişilemiyorsa uygulama çalışmaya devam eder; API ve dashboard açıklayıcı bağlantı durumunu gösterir.

## Interface trafik hesabı

Interface adları, durumları ve hızları IF-MIB üzerinden otomatik keşfedilir. Gelen ve giden Mbps değerleri iki ardışık `ifHCInOctets` / `ifHCOutOctets` 64-bit sayacı arasındaki farkın gerçek geçen süreye bölünmesiyle hesaplanır:

`Mbps = sayaçFarkı × 8 / geçenSaniye / 1.000.000`

İlk ölçümde önceki sayaç bulunmadığından trafik `Ölçülüyor` olarak gösterilir. Sayaç geriye giderse reset olarak değerlendirilir ve negatif değer üretilmez. Kullanım yüzdesi bilinen `ifHighSpeed` değeri üzerinde en yoğun trafik yönüne göre hesaplanır; hız bilinmiyorsa kullanım yüzdesi boş bırakılır.

## API

- `GET /api/monitoring/current`: Son başarılı sistem ve interface SNMP ölçümü ile güncel bağlantı durumu
- `GET /api/history/system?range=1h`: CPU, RAM, session ve bağlantı geçmişi
- `GET /api/history/interfaces/1?range=1h`: Belirtilen SNMP index için trafik geçmişi

Geçmiş endpoint'leri `5m`, `1h`, `6h`, `24h`, `7d` ve `30d` aralıklarını destekler. Sonuçlar UTC zaman damgasıyla artan sırada gelir ve yaklaşık 500 noktaya ortalama alınarak küçültülür; interface sonuçlarında ani yükselişler için `maxTotalMbps` ayrıca korunur.

## SQLite geçmişi

SQLite veritabanı uygulama kökünde `data/fortiscope.db` yolunda oluşturulur. `data` klasörü gerektiğinde otomatik hazırlanır; veritabanı, journal, WAL ve SHM dosyaları Git tarafından yok sayılır. SNMP parolaları veritabanına kaydedilmez.

Varsayılan kayıt sıklığı 10 saniye, retention süresi 30 gündür. Bunlar `appsettings.json` üzerinden değiştirilebilir:

```json
"Monitoring": {
  "PersistenceIntervalSeconds": 10,
  "RetentionDays": 30
}
```

Migration oluşturmak ve veritabanını manuel güncellemek için:

```bash
dotnet ef migrations add MigrationAdi --output-dir Data/Migrations
dotnet ef database update
```

Uygulama başlangıçta bekleyen migration'ları otomatik uygular. Veritabanı işlemi başarısız olsa bile SNMP izleme servisi çalışmaya devam eder.
