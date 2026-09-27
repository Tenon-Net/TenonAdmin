---
name: daisyui
description: daisyUI 5 components and themes for a surface already using daisyUI or an explicitly requested daisyUI task; TenonAdmin templates use Naive UI and Ant Design.
metadata:
  version: 5.6.x
  source: https://daisyui.com/SKILL.md
---

# daisyUI 5
daisyUI 5 is a CSS library for Tailwind CSS 4.
daisyUI 5 provides class names for common UI components, semantic color names and themes.

## When to run this skill:

- Use for an explicitly requested daisyUI task or a surface that already uses daisyUI.
- For TenonAdmin Vue/React templates, use their existing Naive UI/Ant Design components. Installing daisyUI requires a request to adopt that dependency.

## Mandatory reference

| Task | Guide | Note |
|------|-------|------|
Installing daisyUI | [./install/SKILL.md](./install/SKILL.md) | Use only if daisyUI is not already installed in the project.
Using daisyUI class names | [./usage/SKILL.md](./usage/SKILL.md) | MANDATORY. Read this before using any daisyUI class names in the code.
Configuring daisyUI | [./config/SKILL.md](./config/SKILL.md) | Use this if you need to configure daisyUI themes, prefix, logs, or other options. Not required for basic usage but important for advanced customization.
daisyUI colors and themes | [./colors/SKILL.md](./colors/SKILL.md) | MANDATORY. Read this to understand daisyUI color usage rules and how to use daisyUI colors in the code.
daisyUI components | [./components/](./components/) | Read the selected component's docs; compare alternatives only when the choice is unclear.

## List of components

- [accordion](./components/accordion.md)
- [aura](./components/aura.md)
- [alert](./components/alert.md)
- [avatar](./components/avatar.md)
- [badge](./components/badge.md)
- [breadcrumbs](./components/breadcrumbs.md)
- [button](./components/button.md)
- [calendar](./components/calendar.md)
- [card](./components/card.md)
- [carousel](./components/carousel.md)
- [chat](./components/chat.md)
- [checkbox](./components/checkbox.md)
- [collapse](./components/collapse.md)
- [countdown](./components/countdown.md)
- [diff](./components/diff.md)
- [divider](./components/divider.md)
- [dock (app bar)](./components/dock.md)
- [drawer (sidebar)](./components/drawer.md)
- [dropdown](./components/dropdown.md)
- [FAB](./components/fab.md)
- [fieldset](./components/fieldset.md)
- [file-input](./components/file-input.md)
- [filter](./components/filter.md)
- [footer](./components/footer.md)
- [hero](./components/hero.md)
- [hover-3d](./components/hover-3d.md)
- [hover-gallery](./components/hover-gallery.md)
- [indicator](./components/indicator.md)
- [input](./components/input.md)
- [join (group)](./components/join.md)
- [kbd](./components/kbd.md)
- [label](./components/label.md)
- [link](./components/link.md)
- [list](./components/list.md)
- [loading](./components/loading.md)
- [mask](./components/mask.md)
- [megamenu](./components/megamenu.md)
- [menu](./components/menu.md)
- [mockup-browser](./components/mockup-browser.md)
- [mockup-code](./components/mockup-code.md)
- [mockup-phone](./components/mockup-phone.md)
- [mockup-window](./components/mockup-window.md)
- [modal](./components/modal.md)
- [navbar](./components/navbar.md)
- [otp](./components/otp.md)
- [pagination](./components/pagination.md)
- [progress](./components/progress.md)
- [radial-progress](./components/radial-progress.md)
- [radio](./components/radio.md)
- [range](./components/range.md)
- [rating](./components/rating.md)
- [select](./components/select.md)
- [skeleton](./components/skeleton.md)
- [stack](./components/stack.md)
- [stat](./components/stat.md)
- [status](./components/status.md)
- [steps](./components/steps.md)
- [swap](./components/swap.md)
- [tab](./components/tab.md)
- [table](./components/table.md)
- [text-rotate](./components/text-rotate.md)
- [textarea](./components/textarea.md)
- [theme-controller](./components/theme-controller.md)
- [timeline](./components/timeline.md)
- [toast](./components/toast.md)
- [toggle (switch)](./components/toggle.md)
- [tooltip](./components/tooltip.md)
- [validator](./components/validator.md)

### Component discovery protocol

Before writing any daisyUI code, do this in order:

1. Read the request intent, behavior, and shape, not only literal words. Match on meaning.
2. Use the component list in this file to shortlist the best candidate components.
3. Read the relevant candidate docs; compare alternatives when their behavior would change the result.
4. Compare each candidate's description, behavior, syntax, and rules against the request.
5. Select the best component or combination of components and apply their constraints exactly.
6. State which components were chosen and why they match the request.

Semantic matching is required even when wording differs from component names. A component name might be different from the request but still be the best match. Always consider intent and meaning, not only literal words.

If a user explicitly requests a named component and a same-named doc exists, read that component doc first.
