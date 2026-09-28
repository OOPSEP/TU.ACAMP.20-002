# CARBON ONE - Brand Identity & Design System Prompt Specification

## 1. Brand Guidelines & Color Specification
- **Primary Brand Name**: CARBON ONE (or CARBONONE)
- **Concept**: Enterprise Carbon Footprint & GHG Accounting Platform (SaaS)
- **Palette**:
  - **CARBON**: Deep Corporate Navy / Almost Black -> #0F172A (Slate 900)
  - **ONE**: Vibrant Emerald Green -> #10B981 (Emerald 500)
  - **Icon Accent**: Emerald Green -> #10B981
  - **Version Badge**: 2.0 Pill Badge in Emerald Green (BadgeStyle.Success / #10B981)
- **Typography**: Sans-Serif Bold/ExtraBold (Font Weight: 800), Letter Spacing: 0.5px

---

## 2. Image Generation Prompts (AI Tools: Midjourney / DALL-E / Gemini)

### Prompt 1: Navbar Lockup (Approved Version)
> A photorealistic close-up view of the web application top navbar on a modern enterprise SaaS platform. On the crisp white header bar: The green three-bar analytics chart icon (emerald green #10B981, three vertical rounded bars), immediately followed by the brand text where 'CARBON' is in bold dark navy almost black (#0F172A) and 'ONE' is in vibrant emerald green (#10B981), accompanied by a small green rounded badge 'v2.0'. Next to it are the horizontal navigation links 'Home', 'Dashboard', 'Organization', 'Reports' with subtle icons. Clean UI design, high aesthetic, perfectly sharp typography, modern web application.

### Prompt 2: Typography Variations Showcase
> Brand logo typography showcase for 'CARBON ONE'. The word 'CARBON' is in ultra-dark navy almost black font (#0F172A), and the word 'ONE' is in vibrant emerald green (#10B981). Displaying 4 clean horizontal variations on a crisp white background: Variation A: Clean modern sans-serif with 'CARBON' in bold navy-black and 'ONE' in vibrant green, preceded by a stylized green leaf-bar-chart icon. Variation B: Minimalist tech font with 'CARBON' in heavy dark navy and 'ONE' in green with a subtle leaf accent on the letter 'O'. Variation C: Compact lockup with an emerald number 1 integrated beside bold dark navy CARBON. Variation D: High-end corporate layout with small green 'v2.0' pill badge. Ultra crisp vector style, web navbar scale.

### Prompt 3: 4 Icon Concepts (Net Zero, Leaf Chart, Hexagon Ring, C1 Ribbon)
> A clean professional modern corporate brand identity logo showcase for 'CARBON ONE' ESG and Carbon Accounting SaaS Web Platform. Showing 4 distinct minimalist logo variations side by side on a clean white/light gray background. Option 1: Modern geometric leaf and circular carbon cycle forming the number '1', with text 'CARBON ONE' in bold sans-serif font. Option 2: Sleek analytics bar chart blending into a green leaf and the letter C. Option 3: Clean emerald green hexagon carbon ring with numeral '1' inside. Option 4: Minimalist continuous line loop forming C and 1. Color palette: Emerald green (#10B981), mint accent, deep corporate navy (#0F172A). High resolution, vector logo presentation mockup, UI navbar context.

---

## 3. UI Code Implementation Standard (Blazor / HTML)

`html
<NavLink href=" style=text-decoration: none;>
 <RadzenStack Orientation=Orientation.Horizontal AlignItems=AlignItems.Center Gap=6px>
 <RadzenIcon Icon=bar_chart Style=color: #10b981; font-size: 1.5rem; />
 <span style=font-weight: 800; font-size: 1.25rem; letter-spacing: 0.5px; line-height: 1; user-select: none;>
 <span style=color: #0f172a;>CARBON</span><span style=color: #10b981;>ONE</span>
 </span>
 <RadzenBadge Text=v2.0 BadgeStyle=BadgeStyle.Success Style=border-radius: 6px; font-size: 0.7rem; />
 </RadzenStack>
</NavLink>
`