# MSIX packaging

The existing Inno Setup scripts continue to produce the GitHub Release `.exe` installers. This directory produces a separate MSIX package for Microsoft Store submission.

The package declares four supported resource languages: Simplified Chinese (`zh-CN`), Traditional Chinese (`zh-TW`), English (`en-US`), and Japanese (`ja-JP`).

```powershell
powershell -File apps/windows/msix/build.ps1 `
  -IdentityName 'YOUR_PARTNER_CENTER_IDENTITY' `
  -Publisher 'CN=YOUR_PARTNER_CENTER_PUBLISHER' `
  -PublisherDisplayName 'MarkLeaf'
```

The Store identity values are required for a package that can be submitted. A local test package can use the defaults, but it must be signed with a development certificate before installation. The package is self-contained by default and still requires the WebView2 Evergreen Runtime on the machine.

Pass `-Certificate` and, when needed, `-CertificatePassword` to sign the generated package locally.
