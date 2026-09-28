import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import Link from "next/link";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "RelayForge",
  description: "Watch a distributed job scheduler run, one guided demo at a time",
};

const NAV = [
  { href: "/", label: "Overview" },
  { href: "/jobs", label: "Runs" },
  { href: "/concepts", label: "Glossary" },
] as const;

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="en"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="flex min-h-full flex-col">
        <header className="border-b border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900">
          <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-x-4 gap-y-2 px-4 py-3">
            <Link href="/" className="font-semibold">
              RelayForge
            </Link>
            <nav className="flex items-center gap-4 text-sm">
              {NAV.map((item) => (
                <Link key={item.href} href={item.href} className="hover:underline">
                  {item.label}
                </Link>
              ))}
            </nav>
          </div>
        </header>
        <main className="mx-auto w-full max-w-5xl grow px-4 py-8">{children}</main>
        <footer className="border-t border-zinc-200 py-4 dark:border-zinc-800">
          <p className="mx-auto max-w-5xl px-4 text-xs text-zinc-500">
            A learning project: a distributed task scheduler in .NET, with this Next.js
            dashboard in front of it. New here? Start with the tour on the overview page.
          </p>
        </footer>
      </body>
    </html>
  );
}
