# Security Policy - Apex Diagnostics Suite

We take the security and integrity of the **Apex Diagnostics Suite** very seriously. Since this utility operates with raw system access (`kernel32.dll` physical drive pointers, multi-core CPU stress, and low-level diagnostic drivers), we are committed to resolving vulnerabilities quickly and responsibly.

---

## 🛡️ Supported Versions

We actively support and secure the following versions:

| Version | Supported | Notes |
| :--- | :--- | :--- |
| **v1.x** | 🟢 Yes | Active development and security patch branch. |
| **< v1.0** | 🛑 No | Pre-release prototypes. Please upgrade to v1.x immediately. |

---

## 🚨 Reporting a Vulnerability

If you discover a security vulnerability (such as potential buffer overflows in raw handles, access violation bugs, or privilege escalations), please **do not open a public GitHub issue**. Doing so exposes target systems to potential risks before a patch is ready.

Instead, please report it privately:
1. Email the details to the project maintainer: **muzafferilisik@gmail.com**
2. Include a detailed description of the vulnerability, step-by-step instructions to reproduce it, and any logs or screenshots that are helpful.

### 📅 Our Security Response SLA
* **Initial Acknowledgment:** Within **24 to 48 hours**.
* **Vulnerability Investigation & Fix:** Usually within **5 business days**.
* **Public Security Advisory:** Issued after the vulnerability is fully patched and the release is live on GitHub.

We highly appreciate your cooperation in keeping the open-source community safe and secure!
