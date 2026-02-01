# Azure Publish Instructions

## Quick Publish from Visual Studio

1. **Right-click** the `CsvChecker` project → **Publish**
2. Choose **Azure** → **Azure App Service (Windows or Linux)**
3. Sign in with your Azure account
4. Select:
   - **Subscription**: Your Azure subscription
   - **Resource Group**: Create new or use existing (e.g., `csv-checker-rg`)
   - **App Name**: `csv-checker` (or your preferred name)
   - **Region**: Choose closest to your users (e.g., East US)
   - **Hosting Plan**: 
     - **Free (F1)**: For testing only (no custom domain/SSL)
     - **Basic (B1)**: Recommended minimum (~$13/month)
     - **Standard (S1)**: For production (~$70/month)
5. Click **Create** (first time) or **Publish** (subsequent deploys)
6. Wait for deployment to complete

## Post-Publish Configuration (First Time Only)

### 1. Enable WebSockets (Required for Blazor Server)
In Azure Portal:
- Go to your App Service
- **Settings** → **Configuration** → **General settings**
- Set **Web sockets** to **On**
- Click **Save**

### 2. Configure Email Settings
In Azure Portal:
- Go to your App Service
- **Settings** → **Configuration** → **Application settings**
- Add these settings (click **+ New application setting**):
  ```
  EmailSettings__SmtpHost = your-smtp-server.com
  EmailSettings__SmtpPort = 587
  EmailSettings__FromEmail = noreply@csv-checker.com
  EmailSettings__Username = your-username
  EmailSettings__Password = your-password
  ```
- Click **Save**

### 3. Set Up Custom Domain (www.csv-checker.com)
In Azure Portal:
- Go to your App Service
- **Settings** → **Custom domains**
- Click **+ Add custom domain**
- Enter: `www.csv-checker.com`
- Copy the DNS records shown
- Add to your domain registrar:
  - **CNAME**: `www` → `your-app.azurewebsites.net`
- Click **Validate** → **Add**

### 4. Enable SSL Certificate
In Azure Portal:
- Go to your App Service
- **Settings** → **TLS/SSL settings**
- Click **Private Key Certificates (.pfx)** → **+ Create App Service Managed Certificate**
- Select your custom domain
- Click **Create**
- Go to **Bindings** tab → **+ Add TLS/SSL Binding**
- Select your domain and certificate
- Click **Add**

### 5. Force HTTPS (Recommended)
In Azure Portal:
- Go to your App Service
- **Settings** → **Configuration** → **General settings**
- Set **HTTPS Only** to **On**
- Click **Save**

## Update Publish Settings (Subsequent Publishes)

After first publish, Visual Studio saves your profile in:
`Properties/PublishProfiles/[ProfileName].pubxml`

You can now publish anytime by:
1. Right-click project → **Publish**
2. Click **Publish** button

## Monitoring & Troubleshooting

### View Logs
In Azure Portal:
- **Monitoring** → **Log stream** (live logs)
- **Monitoring** → **App Service logs** (configure logging)

### Application Insights (Recommended)
In Azure Portal:
- Go to your App Service
- **Settings** → **Application Insights**
- Click **Turn on Application Insights**
- Create new resource
- Click **Apply**

## Cost Estimates
- **Basic B1**: ~$13/month (1 core, 1.75 GB RAM)
- **Standard S1**: ~$70/month (1 core, 1.75 GB RAM, better performance)
- **Custom domain SSL**: Free (App Service Managed Certificate)
- **Bandwidth**: First 5 GB free, then $0.087/GB

## Pre-Publish Checklist
- [x] Production appsettings configured
- [x] Web.config for Azure created
- [x] Database path handles Azure environment
- [x] Static files (robots.txt, sitemap.xml) in wwwroot
- [ ] Email settings configured in Azure Portal
- [ ] WebSockets enabled
- [ ] Custom domain DNS configured
- [ ] SSL certificate bound

## Need Help?
- Azure Support: https://portal.azure.com → Help + support
- App Service docs: https://docs.microsoft.com/azure/app-service/
