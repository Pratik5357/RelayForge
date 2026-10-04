import type { Metadata } from "next";
import { Archivo, Big_Shoulders, Geist_Mono } from "next/font/google";
import { NavRail } from "@/components/NavRail";
import "./globals.css";

const archivo = Archivo({
  variable: "--font-archivo",
  subsets: ["latin"],
});

const display = Big_Shoulders({
  variable: "--font-display",
  subsets: ["latin"],
  // Next cannot compute fallback metrics for this family and warns on every request.
  adjustFontFallback: false,
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "RelayForge",
  description: "A small distributed job scheduler, built to be watched while it runs.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="en"
      className={`${archivo.variable} ${display.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="min-h-full md:grid md:grid-cols-[14rem_minmax(0,1fr)]">
        <NavRail />
        <main className="mx-auto w-full max-w-6xl px-5 py-8 md:px-10 md:py-10">{children}</main>
      </body>
    </html>
  );
}
