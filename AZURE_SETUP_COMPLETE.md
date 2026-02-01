# ✅ Azure Publish Setup Complete

Your project is now Azure-ready! Here's what was configured:

## Files Created/Modified

### ✅ Production Configuration
- **`appsettings.Production.json`** - Production app settings with Azure-specific database path
- **`appsettings.Email.Production.json`** - Email config template (credentials to be set in Azure Portal)
- **`web.config`** - IIS/Azure App Service configuration

### ✅ Project Configuration
- **`CsvChecker.csproj`** - Updated to:
  - Use InProcess hosting model for better performance
  - Ensure all files (sitemap.xml, robots.txt) are published
  - Exclude local email credentials from publish (security)

### ✅ Application Code
- **`Program.cs`** - Updated to load correct email config based on environment

### ✅ Documentation
- **`AZURE_PUBLISH.md`** - Complete step-by-step Azure publish guide

## 🚀 How to Publish (Quick Steps)

1. **Right-click** `CsvChecker` project in Solution Explorer
2. Select **Publish...**
3. Choose **Azure** → **Azure App Service (Windows or Linux)**
4. Sign in to your Azure account
5. Click **Create New** App Service:
   - **Name**: `csv-checker` (or your choice)
   - **Subscription**: Select your Azure subscription  
   - **Resource Group**: Create new `csv-checker-rg`
   - **Hosting Plan**: Choose **Basic B1** or higher (required for custom domains)
   - **Region**: East US (or closest to your users)
6. Click **Create** and wait for provisioning
7. Click **Publish** to deploy

Visual Studio will create a publish profile and deploy your app automatically!

## ⚙️ Post-Publish Configuration (In Azure Portal)

### 1. Enable WebSockets (Required - 2 minutes)
```
Azure Portal → Your App Service → Configuration → General settings
→ Web sockets: On → Save
```

### 2. Configure Email Settings (Required - 3 minutes)
```
Azure Portal → Your App Service → Configuration → Application settings
→ + New application setting:

Email__SmtpHost = smtp.gmail.com
Email__SmtpPort = 587
Email__FromEmail = noreply@csv-checker.com
Email__FromPassword = [your-app-password]
Email__ToEmail = miker55@gmail.com
Email__FromName = CSV Checker

→ Save
```

### 3. Set Up Custom Domain (5 minutes)
```
Azure Portal → Your App Service → Custom domains
→ + Add custom domain → www.csv-checker.com

At your domain registrar (e.g., GoDaddy, Namecheap):
Add CNAME record: www → [your-app-name].azurewebsites.net

Back in Azure → Validate → Add
```

### 4. Enable Free SSL (2 minutes)
```
Azure Portal → Your App Service → TLS/SSL settings
→ Private Key Certificates → + Create App Service Managed Certificate
→ Select your custom domain → Create
→ Bindings tab → + Add TLS/SSL Binding → Select domain & certificate
→ Add
```

### 5. Force HTTPS (1 minute)
```
Azure Portal → Your App Service → Configuration → General settings
→ HTTPS Only: On → Save
```

## 🔍 What Happens on Publish

- ✅ Builds in Release configuration
- ✅ Includes `sitemap.xml` and `robots.txt` (SEO ready)
- ✅ Uses production database path (`/home/site/data/telemetry.sqlite`)
- ✅ Loads production email config (credentials from Azure Portal)
- ✅ Excludes local development email credentials (security)
- ✅ Runs EF Core migrations automatically on startup

## 📊 Expected Costs

| Service | Tier | Monthly Cost |
|---------|------|--------------|
| App Service | Basic B1 (1 core, 1.75GB) | ~$13 |
| App Service | Standard S1 (better perf) | ~$70 |
| Custom Domain SSL | App Service Managed | **FREE** |
| Bandwidth | First 5 GB | **FREE** |
| Bandwidth | Additional per GB | $0.087 |

**Recommended starter**: Basic B1 (~$13/month)

## 🎯 Your URLs After Publish

- **Azure URL**: `https://csv-checker.azurewebsites.net`
- **Custom Domain**: `https://www.csv-checker.com` (after DNS config)
- **Sitemap**: `https://www.csv-checker.com/sitemap.xml`
- **Robots**: `https://www.csv-checker.com/robots.txt`

## 🛡️ Security Notes

- ✅ Local email credentials (`appsettings.Email.json`) are excluded from publish
- ✅ Production uses placeholder credentials - configure in Azure Portal
- ✅ `.gitignore` protects local email config from being committed
- ✅ Database stored in Azure persistent storage (`/home/site/data/`)

## 📝 Next Steps After First Publish

1. Test the site at your Azure URL
2. Configure email settings in Azure Portal
3. Enable WebSockets
4. Set up custom domain DNS
5. Enable SSL certificate
6. Submit sitemap to Google Search Console
7. (Optional) Enable Application Insights for monitoring

## 🆘 Troubleshooting

### Site doesn't load / 502 error
- Check WebSockets are enabled
- View logs: Azure Portal → Log stream

### Email not sending
- Verify Application Settings in Azure Portal
- Check email settings format: `Email__SmtpHost` (double underscore)

### Database issues
- Database auto-creates at `/home/site/data/telemetry.sqlite`
- View logs to confirm migrations ran successfully

## 📚 Full Instructions

See `AZURE_PUBLISH.md` for complete step-by-step guide with screenshots references.

---

**You're all set!** Just right-click → Publish → Azure → Create/Select App Service → Publish 🚀
