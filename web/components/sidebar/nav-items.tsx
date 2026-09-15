"use client"

import {
  Folder,
  MoreHorizontal,
  Share,
  Trash2,
  type LucideIcon,
} from "lucide-react"

import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import {
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuAction,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from "@/components/ui/sidebar"
import Link from "next/link"

export function NavItems({
  projects,
  pathname
}: {
  projects: {
    name: string
    url: string
    icon: LucideIcon
  }[];
  pathname: string
}) {
  const { isMobile } = useSidebar()

  return (
    <SidebarGroup>

      <SidebarMenu className="gap-2">
        {projects.map((item) => (
          <SidebarMenuItem key={item.name}>
            <SidebarMenuButton isActive={pathname.includes(item.url)} className="transition-all py-3">
              <Link href={item.url} className="flex items-center gap-2 w-full ">
                <item.icon />
                <span className="group-data-[collapsible=icon]:hidden">{item.name}</span>
              </Link>
            </SidebarMenuButton>
          </SidebarMenuItem>
        ))}
      </SidebarMenu>
    </SidebarGroup>
  )
}
