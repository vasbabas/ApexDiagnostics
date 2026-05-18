# Contributing to Apex Diagnostics Suite

Thank you for your interest in contributing to the **Apex Diagnostics Suite**! We welcome contributions from developers, technicians, and system engineers. 

To maintain the high quality, reliability, and security of this hardware diagnostics platform, please follow these guidelines.

---

## 🚀 Branching & Pull Request Workflow

We enforce a strict **Branch Protection Policy** to ensure our main codebase (`main`) remains 100% stable at all times.

### 1. Development Process
1. **Fork** the repository and clone your fork locally.
2. Create a dedicated development branch for your changes:
   * For new features: `feature/your-feature-name`
   * For bug fixes: `bugfix/issue-description`
   * For optimizations: `perf/optimization-description`
3. Implement your changes. Always verify that your changes compile successfully with `0 warnings and 0 errors` in VS Code or Visual Studio.
4. Push your branch to your fork on GitHub.
5. Open a **Pull Request (PR)** against our upstream `main` branch.

### 2. Pull Request Requirements
* Provide a clear description of what the PR accomplishes and why it is needed.
* Ensure all C# and XAML files comply with our styling rules (see below).
* Maintain complete backward-compatibility with bootable **WinPE** environments (e.g. avoid dependencies on local desktop installers, complex external DLLs, or internet connections).
* At least one repository maintainer must review and approve the PR before it is merged.

---

## 🎨 Coding & Design Standards

### 1. C# Styling Rules
* **Pattern:** We strictly utilize the **MVVM (Model-View-ViewModel)** design pattern. Avoid adding logic inside XAML code-behinds (`.xaml.cs`) unless it is purely visual UI-interaction behavior.
* **Naming Conventions:**
  * Use **PascalCase** for Class names, Methods, properties, and Interfaces (e.g., `CircularGauge`, `TelemetryManager`, `IGetTelemetries`).
  * Use **camelCase** with a leading underscore for private backing fields (e.g., `_cpuTemperature`).
  * Interfaces should always start with a capital `I` (e.g., `ISafetyWatchdog`).
* **Asynchronous Operations:** All heavy telemetry scanning, CPU stress, and disk handles must run **asynchronously** using `async/await` tasks to ensure the WPF UI never freezes or stutters.

### 2. XAML & Resource Dictionaries
* **Dynamic Styling:** Do not hardcode static colors or strings in UI elements. Use `DynamicResource` tags linked to `Themes/DarkTheme.xaml` and localization keys linked to `Resources/Strings.xx.xaml` files.
* **Auto-Wrapping Labels:** Ensure all textual labels in user controls support auto-wrapping (`TextWrapping="Wrap"`) to prevent visual clipping in different languages.

---

## 📄 Licensing & Agreements
By contributing your code to the **Apex Diagnostics Suite**, you agree to license your contributions under the **MIT License**.
