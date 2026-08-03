import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "__PROJECT_DISPLAY_NAME__",
  description: "Official website of __PROJECT_DISPLAY_NAME__.",
};

interface RootLayoutProps {
  children: React.ReactNode;
}

export default function RootLayout({ children }: RootLayoutProps) {
  return (
    <html lang="hu">
      <body>{children}</body>
    </html>
  );
}