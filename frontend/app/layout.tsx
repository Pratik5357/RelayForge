import type { Metadata } from "next";
import { Atkinson_Hyperlegible_Mono, Atkinson_Hyperlegible_Next } from "next/font/google";
import { TopBar } from "@/components/TopBar";
import { Toaster } from "@/components/Toaster";
import "./globals.css";

// Next has no precomputed fallback metrics for the Atkinson families, so skip the adjusted fallback.
// Turbopack still logs "Failed to find font override values" for them anyway; it's harmless.
const atkinson = Atkinson_Hyperlegible_Next({
  variable: "--font-atkinson",
  subsets: ["latin"],
  adjustFontFallback: false,
});

const atkinsonMono = Atkinson_Hyperlegible_Mono({
  variable: "--font-atkinson-mono",
  subsets: ["latin"],
  adjustFontFallback: false,
});

export const metadata: Metadata = {
  title: "RelayForge",
  description: "A small distributed job scheduler, built to be watched while it runs.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${atkinson.variable} ${atkinsonMono.variable} h-full antialiased`}>
      <body className="min-h-full">
        <Toaster>
          <TopBar />
          <main className="mx-auto w-full max-w-[1320px] px-4 pb-24 pt-10 sm:px-6 md:pt-14 lg:px-10">
            {children}
          </main>
        </Toaster>
      </body>
    </html>
  );
}
